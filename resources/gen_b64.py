import base64
import os

res_dir = r"c:\Temp\BOM_Manager\resources"

def get_b64(filename):
    p = os.path.join(res_dir, filename)
    if os.path.exists(p):
        with open(p, "rb") as f:
            return base64.b64encode(f.read()).decode("utf-8")
    return ""

b64_32 = get_b64("icon_32.png")
b64_40 = get_b64("icon_40.png")
b64_64 = get_b64("icon_64.png")

with open(os.path.join(res_dir, "icons_b64.py"), "w", encoding="utf-8") as out:
    out.write(f'LOGO_PNG_B64_32 = "{b64_32}"\n')
    out.write(f'LOGO_PNG_B64_40 = "{b64_40}"\n')
    out.write(f'LOGO_PNG_B64_64 = "{b64_64}"\n')

print("icons_b64.py generated successfully!")
