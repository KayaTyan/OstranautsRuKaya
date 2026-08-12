#!/usr/bin/env python3
"""
OstranautsRuKaya Remaining Translation Pipeline
================================================
Переводит непереведённые display-поля через Z.AI API.
ОСТОРОЖНО: переводит ТОЛЬКО указанные display-поля, не трогает структурные.

Usage:
  python3 translate_remaining.py --test     # тест на 5 записях
  python3 translate_remaining.py --run      # полный прогон
  python3 translate_remaining.py --apply    # применить переведённое
"""

import json
import re
import os
import glob
import time
import sys
import urllib.request
import urllib.error
from collections import defaultdict

# === CONFIG ===
MOD_ROOT = "/srv/OstranautsRuKaya/complete/data"
GAME_ROOT = "/tmp/Ostranauts_Data_1.0.0.9/Ostranauts_Data/StreamingAssets/data"
QUEUE_FILE = "/srv/OstranautsRuKaya/translation_queue.json"
RESULT_FILE = "/srv/OstranautsRuKaya/translation_results.json"
API_URL = "https://api.z.ai/api/anthropic/v1/messages"
API_KEY = None  # loaded from .env
MODEL = "glm-4.5-air"
BATCH_SIZE = 40  # entries per API call
MAX_TOKENS = 8000  # large enough for long descriptions

# Fields that SHOULD be translated
DISPLAY_FIELDS = {
    'strDesc', 'strNameFriendly', 'strNameShort',
    'strFriendlyName', 'strDescription', 'strTitle',
    'strBody', 'strTooltip'
}

# === UTILITIES ===

def load_api_key():
    """Load GLM API key from Hermes .env"""
    global API_KEY
    with open("/root/.hermes/.env") as f:
        for line in f:
            if line.startswith("GLM_API_KEY="):
                API_KEY = line.strip().split("=", 1)[1]
                return
    raise RuntimeError("GLM_API_KEY not found in /root/.hermes/.env")

def safe_load(path):
    with open(path, 'rb') as f:
        raw = f.read()
    text = raw.decode('utf-8')
    text = re.sub(r'[\x00-\x1f]', '', text)
    try:
        return json.loads(text)
    except:
        return None

def has_cyrillic(s):
    if not isinstance(s, str):
        return False
    return any('\u0400' <= c <= '\u04ff' for c in s)

# === STEP 1: Extract untranslated fields ===

def extract_untranslated():
    """Find all untranslated display fields and build queue."""
    queue = []
    for fpath in sorted(glob.glob(f"{MOD_ROOT}/**/*.json", recursive=True)):
        rel = os.path.relpath(fpath, MOD_ROOT)
        data = safe_load(fpath)
        if data is None:
            continue
        items = data if isinstance(data, list) else [data]
        for idx, item in enumerate(items):
            if not isinstance(item, dict):
                continue
            strname = item.get('strName', '')
            for field in DISPLAY_FIELDS:
                if field not in item:
                    continue
                val = item[field]
                if not isinstance(val, str) or not val.strip():
                    continue
                if len(val.strip()) <= 2:
                    continue
                if has_cyrillic(val):
                    continue  # already translated
                queue.append({
                    'id': len(queue),
                    'file': rel,
                    'idx': idx,
                    'strName': strname,
                    'field': field,
                    'en': val,
                })
    
    with open(QUEUE_FILE, 'w', encoding='utf-8') as f:
        json.dump(queue, f, ensure_ascii=False, indent=2)
    
    print(f"[extract] {len(queue)} untranslated fields → {QUEUE_FILE}")
    return queue

# === STEP 2: Translate via API ===

def call_api(prompt, max_retries=3):
    """Call Z.AI Anthropic API."""
    data = json.dumps({
        "model": MODEL,
        "max_tokens": MAX_TOKENS,
        "messages": [{"role": "user", "content": prompt}],
    }).encode('utf-8')
    
    req = urllib.request.Request(API_URL, data=data, method='POST')
    req.add_header('Authorization', f'Bearer {API_KEY}')
    req.add_header('Content-Type', 'application/json')
    
    for attempt in range(max_retries):
        try:
            with urllib.request.urlopen(req, timeout=120) as resp:
                result = json.loads(resp.read().decode('utf-8'))
                return result
        except urllib.error.HTTPError as e:
            body = e.read().decode('utf-8') if e.fp else ''
            print(f"  [api] HTTP {e.code}: {body[:200]}")
            if e.code == 429 or e.code == 529:
                wait = 10 * (attempt + 1)
                print(f"  [api] Rate limited, waiting {wait}s...")
                time.sleep(wait)
                continue
            raise
        except Exception as e:
            print(f"  [api] Error: {e}")
            if attempt < max_retries - 1:
                time.sleep(5)
                continue
            raise
    raise RuntimeError(f"API failed after {max_retries} retries")

def translate_batch(batch):
    """Translate a batch of entries. Returns list of {id, ru}."""
    # Build prompt
    lines = []
    for entry in batch:
        lines.append(f"[{entry['id']}] {entry['en']}")
    
    prompt = f"""Ты профессиональный переводчик. Переведи следующие тексты из космической игры Ostranauts на русский язык.

Это ОПИСАНИЯ, ДИАЛОГИ и НАЗВАНИЯ для игрока. Переводи их ВСЕ, включая короткие слова.

КРИТИЧЕСКИЕ ПРАВИЛА СОХРАНЕНИЯ ТОКЕНОВ:
- Токены в [квадратных скобках] — это КОДЫ ИГРОВОГО ДВИЖКА, они заменяются автоматически
- НЕ меняй содержимое скобок: [us] должен остаться [us], НЕ [us-pos]
- НЕ удаляй и не добавляй токены: если в оригинале 1× [us], в переводе должен быть 1× [us]
- Токены: [us], [them], [has], [is], [us-pos], [them-pos], [he], [she]
- Строй фразу так, чтобы токен был в правильном месте: "[us] [has] ордер на арест"
- НЕ переводи названия фракций: BCRS, OKLG, JATL, GalCon, HQCH, EJDR, MVOL, MTRS, SVIR, JFTS, JPTN
- НЕ переводи названия станций: K-Leg, Atlantis, Fort Simpson, Qincheng, Hangzhou

Примеры правильного перевода:
[us] [has] an outstanding arrest warrant → [us] [has] действующий ордер на арест
[us] [is] wanted dead or alive → [us] разыскивается живым или мёртвым

Тексты:

{chr(10).join(lines)}

Ответ — только переведённый текст в формате [ID] перевод:"""
    
    result = call_api(prompt)
    
    # Parse response
    text = ""
    for block in result.get('content', []):
        if block.get('type') == 'text':
            text += block['text']
    
    # Parse response - use string keys throughout
    translations = {}
    current_id = None
    current_text = []
    
    for line in text.split('\n'):
        m = re.match(r'^\[?(\d+)\]?\s*(.*)', line.strip())
        if m:
            if current_id is not None:
                translations[current_id] = '\n'.join(current_text).strip()
            current_id = m.group(1)  # keep as string!
            current_text = [m.group(2)]
        elif current_id is not None and line.strip():
            current_text.append(line.strip())
    
    if current_id is not None:
        translations[current_id] = '\n'.join(current_text).strip()
    
    return translations

def run_translation(queue, test_mode=False):
    """Translate all entries in queue."""
    if test_mode:
        # Pick 5 diverse entries
        sample = []
        for cat in ['conditions', 'condowners', 'cooverlays', 'interactions', 'pledges']:
            for entry in queue:
                if cat in entry['file'] and len(entry['en']) > 10:
                    sample.append(entry)
                    break
        queue = sample[:5]
        print(f"[test] Selected {len(queue)} entries for test")
    
    results = {}
    
    # Load existing results if any
    if os.path.exists(RESULT_FILE) and not test_mode:
        with open(RESULT_FILE, encoding='utf-8') as f:
            results = json.load(f)
        print(f"[resume] Loaded {len(results)} existing translations")
    
    total = len(queue)
    batch_count = 0
    
    for i in range(0, total, BATCH_SIZE):
        batch = queue[i:i + BATCH_SIZE]
        
        # Skip already translated
        new_batch = [e for e in batch if str(e['id']) not in results]
        if not new_batch:
            continue
        
        batch_count += 1
        print(f"[batch {batch_count}] {len(new_batch)} entries (ids {new_batch[0]['id']}-{new_batch[-1]['id']})...")
        
        try:
            translations = translate_batch(new_batch)
            
            matched = 0
            for entry in new_batch:
                eid = str(entry['id'])
                if eid in translations:
                    ru = translations[eid]
                    # Validate: check no structural codes were broken
                    en = entry['en']
                    # Count [us], [them] etc in original
                    for token in ['[us]', '[them]', '[has]', '\\n']:
                        en_count = en.count(token)
                        ru_count = ru.count(token)
                        if en_count != ru_count:
                            print(f"  [warn] {entry['strName']}.{entry['field']}: token mismatch '{token}': {en_count}→{ru_count}")
                    
                    results[eid] = {
                        'file': entry['file'],
                        'strName': entry['strName'],
                        'field': entry['field'],
                        'en': en,
                        'ru': ru,
                    }
                    matched += 1
            
            print(f"  [ok] {matched}/{len(new_batch)} translated")
            
            # Save periodically
            if batch_count % 5 == 0:
                with open(RESULT_FILE, 'w', encoding='utf-8') as f:
                    json.dump(results, f, ensure_ascii=False, indent=2)
                print(f"  [save] {len(results)} translations saved")
            
        except Exception as e:
            print(f"  [error] {e}")
            # Save what we have
            with open(RESULT_FILE, 'w', encoding='utf-8') as f:
                json.dump(results, f, ensure_ascii=False, indent=2)
            time.sleep(5)
            continue
        
        # Rate limit delay
        time.sleep(1)
    
    # Final save
    with open(RESULT_FILE, 'w', encoding='utf-8') as f:
        json.dump(results, f, ensure_ascii=False, indent=2)
    
    print(f"\n[done] {len(results)} translations saved to {RESULT_FILE}")
    return results

# === STEP 3: Apply translations ===

def apply_translations():
    """Apply translated fields back to JSON files."""
    if not os.path.exists(RESULT_FILE):
        print("[error] No results file found. Run translation first.")
        return
    
    with open(RESULT_FILE, encoding='utf-8') as f:
        results = json.load(f)
    
    print(f"[apply] {len(results)} translations to apply")
    
    # Group by file
    by_file = defaultdict(list)
    for eid, data in results.items():
        by_file[data['file']].append(data)
    
    total_applied = 0
    
    for rel, entries in sorted(by_file.items()):
        fpath = f"{MOD_ROOT}/{rel}"
        data = safe_load(fpath)
        if data is None:
            print(f"  [skip] {rel}: can't load")
            continue
        
        items = data if isinstance(data, list) else [data]
        applied = 0
        
        for entry in entries:
            for item in items:
                if not isinstance(item, dict):
                    continue
                if item.get('strName') == entry['strName']:
                    field = entry['field']
                    if field in item and item[field] == entry['en']:
                        item[field] = entry['ru']
                        applied += 1
                        break
                    elif field in item:
                        # English text changed since extraction
                        print(f"  [warn] {rel}: {entry['strName']}.{field} text mismatch, skipping")
                        break
        
        if applied > 0:
            with open(fpath, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
            total_applied += applied
            print(f"  {rel}: {applied} fields updated")
    
    print(f"\n[done] {total_applied} fields applied across {len(by_file)} files")

# === MAIN ===

if __name__ == '__main__':
    load_api_key()
    
    if '--test' in sys.argv:
        print("=== TEST MODE ===")
        if not os.path.exists(QUEUE_FILE):
            queue = extract_untranslated()
        else:
            with open(QUEUE_FILE, encoding='utf-8') as f:
                queue = json.load(f)
            print(f"[queue] Loaded {len(queue)} entries from {QUEUE_FILE}")
        run_translation(queue, test_mode=True)
        # Show test results
        if os.path.exists(RESULT_FILE):
            with open(RESULT_FILE, encoding='utf-8') as f:
                results = json.load(f)
            print("\n=== TEST RESULTS ===")
            for eid, data in list(results.items())[-5:]:
                print(f"\n[{eid}] {data['strName']}.{data['field']}")
                print(f"  EN: {data['en'][:80]}...")
                print(f"  RU: {data['ru'][:80]}...")
    
    elif '--run' in sys.argv:
        print("=== FULL TRANSLATION RUN ===")
        if not os.path.exists(QUEUE_FILE):
            queue = extract_untranslated()
        else:
            with open(QUEUE_FILE, encoding='utf-8') as f:
                queue = json.load(f)
            print(f"[queue] Loaded {len(queue)} entries from {QUEUE_FILE}")
        run_translation(queue)
    
    elif '--apply' in sys.argv:
        print("=== APPLY TRANSLATIONS ===")
        apply_translations()
    
    elif '--extract' in sys.argv:
        extract_untranslated()
    
    else:
        print("Usage: python3 translate_remaining.py [--extract|--test|--run|--apply]")
