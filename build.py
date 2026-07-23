#!/usr/bin/env python3
"""
OstranautsRuKaya Builder
=========================
Денис скидывает файл в формате: EN = {RU}
Скрипт сам:
  1. Парсит переводы
  2. Обновляет словарь в Main.cs
  3. Обновляет хардкод MFD-патчей
  4. Компилирует DLL
  5. Пересобирает publish.7z
  6. Залиливает на GitHub dev release

Usage: python3 build.py translations.txt
"""

import sys
import os
import re
import subprocess
import json
import tempfile
import shutil

REPO = "/srv/OstranautsRuKaya"
SRC = f"{REPO}/src/Main.cs"
MANAGED = "/tmp/Ostranauts_Data_Read_Only/Ostranauts_Data/Managed"
BEPINEX = "/tmp/publish_serjo2_test/publish/BepInEx/core"
DLL_OUT = "/tmp/kaya_build/OstranautsRuKaya.dll"
TOKEN_FILE = "/tmp/kayatyan_token.txt"
REPO_GH = "KayaTyan/OstranautsRuKaya"
RELEASE_TAG = "dev"

def log(msg):
    print(f"[build] {msg}")

def parse_translations(filepath):
    """Parse 'EN = {RU}' format → dict {EN: RU}"""
    result = {}
    with open(filepath, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            # Format: EN = {RU}
            m = re.match(r'^(.*?)\s*=\s*\{(.*)\}$', line)
            if m:
                en = m.group(1).strip()
                ru = m.group(2).strip()
                result[en] = ru
    log(f"Parsed {len(result)} translations from {filepath}")
    return result

def update_dictionary(src, translations):
    """Replace the HudTranslations dictionary content in Main.cs"""
    # The dictionary is between the opening { and closing };
    # Pattern: internal static readonly Dictionary<string, string> HudTranslations = new Dictionary...()\n        {\n ... \n        };
    pattern = r'(internal static readonly Dictionary<string, string> HudTranslations = new Dictionary<string, string>\(System\.StringComparer\.Ordinal\)\s*\{)\n(.*?)(\n\s*\};)'
    
    # Build new entries
    lines = []
    for en, ru in sorted(translations.items()):
        # Escape for C# string
        en_esc = en.replace("\\", "\\\\").replace('"', '\\"')
        ru_esc = ru.replace("\\", "\\\\").replace('"', '\\"')
        lines.append(f'            {{"{en_esc}", "{ru_esc}"}},')
    new_body = "\n".join(lines)
    
    new_src = re.sub(pattern, r'\1\n' + new_body + r'\3', src, flags=re.DOTALL)
    
    if new_src == src:
        log("WARNING: Dictionary replacement didn't match!")
    else:
        log(f"Dictionary updated with {len(translations)} entries")
    
    return new_src

def update_hardcoded_patches(src, translations):
    """Update hardcoded string replacements in MFD patches."""
    count = 0
    # Pattern 1: __result = "RU" (in get_Title/get_Objective patches)
    for en, ru in translations.items():
        # Skip if no hardcoded pattern likely matches
        # These patterns are for specific patches like:
        # if (__result == "EN") __result = "RU";
        old_pattern = f'== "{en}") __result = "{translations.get(en, en)}"'
        # We can't know the old RU value, so we do targeted replacements
        pass
    
    # Instead, do targeted string replacements for known hardcoded spots
    # These are outside the dictionary, in MFD patches
    hardcoded_map = {
        # MFDMainMenu Title
        '"MAIN MENU") __result = "': None,  # special handling
    }
    
    # Approach: find all __result = "..." patterns and update if the key matches
    def replace_result(match):
        nonlocal count
        # match: __result = "RU text"
        ru_text = match.group(1)
        # Check if this RU text appears as a value in translations
        for en, ru in translations.items():
            if ru == ru_text:
                # This is a translation we might need to update — but we don't know the new value
                # Actually we should look at what key it corresponds to
                return match.group(0)
        return match.group(0)
    
    # Better approach: do targeted find-replace for all RU values that changed
    # We need both old and new values, which we get from comparing with current source
    # For now, let's just replace the specific known hardcoded patches
    
    # MFDMainMenu.get_Title
    # Find: if (__result == "MAIN MENU") __result = "...";
    for en_key in ["MAIN MENU"]:
        if en_key in translations:
            pattern = rf'(if \(__result == "{re.escape(en_key)}"\) __result = )"([^"]*)"'
            new_val = translations[en_key]
            new_src, n = re.subn(pattern, rf'\1"{new_val}"', src)
            if n > 0:
                src = new_src
                count += n

    # MFDTranslate.ReplaceInList calls — update target RU strings
    # Pattern: ReplaceInList(xxx, "EN key", "RU value")
    def replace_in_list(match):
        nonlocal count
        prefix = match.group(1)  # MFDTranslate.ReplaceInList(xxx, "EN key", "
        en_key = match.group(2)
        old_ru = match.group(3)
        if en_key in translations:
            new_ru = translations[en_key]
            if new_ru != old_ru:
                count += 1
                return f'{prefix}"{en_key}", "{new_ru}")'
        return match.group(0)
    
    src = re.sub(
        r'(MFDTranslate\.ReplaceInList\([^,]+,\s*")([^"]+)("\s*,\s*")([^"]*)("\))',
        lambda m: f'{m.group(1)}{m.group(2)}{m.group(3)}{translations.get(m.group(2), m.group(4))}{m.group(5)}',
        src
    )
    
    log(f"Updated {count} hardcoded patch references")
    return src

def update_simple_result_patches(src, translations):
    """Update __result = "RU" patches (tutorial objectives, status messages, etc.)"""
    count = 0
    
    # Pattern: __result = "some russian text";
    # We need to find EN→RU mapping. These patches look like:
    # if (__result == "EN text") __result = "RU text";
    # OR just: __result = "RU text"; (in some Postfix patches)
    
    # For the if/then pattern:
    def replace_if_then(match):
        nonlocal count
        en = match.group(2)
        if en in translations:
            new_ru = translations[en]
            old = match.group(3)
            if old != new_ru:
                count += 1
                return f'{match.group(1)}"{en}") __result = "{new_ru}";'
        return match.group(0)
    
    src = re.sub(
        r'((?:if\s*\()?__result\s*==\s*)"([^"]+)"\)?\s*__result\s*=\s*"([^"]*)";',
        replace_if_then,
        src
    )
    
    # For standalone __result = "RU text"; patches where we know the EN equivalent
    # These are in patches like get_ObjectiveName where __result is set directly
    # We'll match by known EN→RU pairs
    for en, new_ru in translations.items():
        # Find the old RU value currently in source and replace
        # This is tricky without knowing old value, skip for now
        pass
    
    log(f"Updated {count} if/then result patches")
    return src

def compile_dll():
    """Compile Main.cs → OstranautsRuKaya.dll"""
    os.makedirs(os.path.dirname(DLL_OUT), exist_ok=True)
    
    refs = [
        f"-r:{MANAGED}/mscorlib.dll",
        f"-r:{MANAGED}/netstandard.dll",
        f"-r:{MANAGED}/System.dll",
        f"-r:{MANAGED}/System.Core.dll",
        f"-r:{MANAGED}/Assembly-CSharp.dll",
        f"-r:{MANAGED}/UnityEngine.dll",
        f"-r:{MANAGED}/UnityEngine.CoreModule.dll",
        f"-r:{MANAGED}/UnityEngine.IMGUIModule.dll",
        f"-r:{MANAGED}/UnityEngine.UI.dll",
        f"-r:{MANAGED}/UnityEngine.TextRenderingModule.dll",
        f"-r:{MANAGED}/Unity.TextMeshPro.dll",
        f"-r:{BEPINEX}/0Harmony.dll",
        f"-r:{BEPINEX}/BepInEx.dll",
    ]
    
    cmd = ["mcs", "-nostdlib", "-target:library", f"-out:{DLL_OUT}",
           "-langversion:latest"] + refs + [SRC]
    
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
    
    if result.returncode != 0:
        errors = [l for l in result.stderr.split("\n") if "error CS" in l]
        log(f"❌ Compilation failed: {len(errors)} errors")
        for e in errors[:10]:
            log(f"  {e}")
        return False
    
    size = os.path.getsize(DLL_OUT)
    log(f"✅ DLL compiled: {size:,} bytes")
    return True

def build_publish_7z():
    """Swap DLL in existing publish.7z, repack with max compression"""
    work = "/tmp/publish_work"
    if os.path.exists(work):
        shutil.rmtree(work)
    os.makedirs(work)
    
    # Download current publish.7z
    log("Downloading current publish.7z...")
    subprocess.run(["curl", "-sL", "-o", "/tmp/current_publish.7z",
                    f"https://github.com/{REPO_GH}/releases/download/{RELEASE_TAG}/publish.7z"],
                   capture_output=True, timeout=120)
    
    # Extract
    subprocess.run(["7z", "x", f"-o{work}", "/tmp/current_publish.7z"],
                   capture_output=True, timeout=120)
    
    # Swap DLL
    dll_path = f"{work}/publish/BepInEx/plugins/OstranautsRuKaya.dll"
    shutil.copy2(DLL_OUT, dll_path)
    log(f"DLL swapped in publish structure")
    
    # Repack with max compression (same settings as original)
    # CRITICAL: delete output first — 7z 'a' appends to existing archive!
    output_7z = "/tmp/publish_new.7z"
    if os.path.exists(output_7z):
        os.remove(output_7z)
    
    log("Repacking publish.7z (max compression, this takes ~40s)...")
    result = subprocess.run([
        "7z", "a", "-t7z",
        "-mx=9",
        "-m0=LZMA2:d26",
        "-mfb=64",
        "-ms=on",
        output_7z,
        f"{work}/publish/"
    ], capture_output=True, text=True, timeout=600)
    
    if result.returncode != 0:
        log(f"❌ 7z repack failed: {result.stderr[:300]}")
        return False
    
    size = os.path.getsize("/tmp/publish_new.7z")
    log(f"✅ publish.7z: {size:,} bytes ({size/1024/1024:.1f} MB)")
    return True

def upload_to_github():
    """Upload publish.7z to GitHub dev release"""
    with open(TOKEN_FILE) as f:
        token = f.read().strip()
    
    # Get release
    r = subprocess.run(["curl", "-s", "-H", f"Authorization: token {token}",
                        f"https://api.github.com/repos/{REPO_GH}/releases/tags/{RELEASE_TAG}"],
                      capture_output=True, text=True)
    data = json.loads(r.stdout)
    release_id = data["id"]
    
    # Delete old asset
    for asset in data.get("assets", []):
        if asset["name"] == "publish.7z":
            subprocess.run(["curl", "-s", "-X", "DELETE",
                            "-H", f"Authorization: token {token}",
                            f"https://api.github.com/repos/{REPO_GH}/releases/assets/{asset['id']}"],
                          capture_output=True, text=True)
            log(f"Deleted old asset {asset['id']}")
            break
    
    # Upload new
    file_size = os.path.getsize("/tmp/publish_new.7z")
    log(f"Uploading {file_size/1024/1024:.1f} MB...")
    
    r = subprocess.run([
        "curl", "-s", "-w", "\n%{http_code}",
        "-X", "POST",
        "-H", f"Authorization: token {token}",
        "-H", "Content-Type: application/x-7z-compressed",
        "--data-binary", "@/tmp/publish_new.7z",
        f"https://uploads.github.com/repos/{REPO_GH}/releases/{release_id}/assets?name=publish.7z"
    ], capture_output=True, text=True, timeout=300)
    
    lines = r.stdout.rsplit("\n", 1)
    http_code = lines[-1]
    body = lines[0] if len(lines) > 1 else ""
    
    if http_code == "201":
        data = json.loads(body)
        url = data.get("browser_download_url", "")
        log(f"✅ Uploaded: {url}")
        return url
    else:
        log(f"❌ Upload failed: HTTP {http_code}: {body[:200]}")
        return None

def main():
    if len(sys.argv) < 2:
        print("Usage: python3 build.py translations.txt [path/to/Main.cs]")
        print("  translations.txt: file in 'EN = {RU}' format")
        sys.exit(1)
    
    trans_file = sys.argv[1]
    if not os.path.exists(trans_file):
        print(f"ERROR: {trans_file} not found")
        sys.exit(1)
    
    log("=== OstranautsRuKaya Builder ===")
    
    # 1. Parse translations
    translations = parse_translations(trans_file)
    
    # 2. Read source
    with open(SRC, encoding="utf-8") as f:
        src = f.read()
    
    # 3. Update dictionary
    src = update_dictionary(src, translations)
    
    # 4. Update hardcoded patches
    src = update_hardcoded_patches(src, translations)
    src = update_simple_result_patches(src, translations)
    
    # 5. Write source
    with open(SRC, "w", encoding="utf-8") as f:
        f.write(src)
    log("Source updated")
    
    # 6. Compile
    if not compile_dll():
        sys.exit(1)
    
    # 7. Build publish.7z
    if not build_publish_7z():
        sys.exit(1)
    
    # 8. Git commit & push
    subprocess.run(["git", "add", "-A"], capture_output=True, cwd=REPO)
    subprocess.run(["git", "commit", "-m", f"update: {len(translations)} HUD translations"],
                   capture_output=True, cwd=REPO)
    subprocess.run(["git", "push", "origin", "dev"], capture_output=True, cwd=REPO)
    log("Git pushed")
    
    # 9. Upload
    url = upload_to_github()
    
    # 10. Done
    log(f"\n{'='*50}")
    if url:
        log(f"🎉 DONE! Download: {url}")
    else:
        log("Upload failed, but DLL is ready at /tmp/kaya_build/OstranautsRuKaya.dll")

if __name__ == "__main__":
    main()
