#!/usr/bin/env python3
"""Genere les assets du bloc Microcontroleur (ArduSiege, M1).
- microcontroller.obj : cube 0.5, convention OBJ droite (CCW vu de l'exterieur),
  chargeur du jeu : indices 1-based positifs uniquement, triangulation en eventail.
- microcontroller.png : texture 32x32 style "microcontroleur" (corps PCB, die central,
  broches dorees). PNG RGBA ecrit a la main (stdlib : struct + zlib).
Usage : python3 gen_assets.py  (depuis n'importe ou ; ecrit dans ./)
"""
import struct, zlib

# ---------------- OBJ : cube (entier ! voir note) ----------------
# IMPORTANT : le chargeur OBJ du jeu (AssetImporter.LoadMeshData) parse les floats
# avec Convert.ToSingle SANS culture invariante -> sur une locale francaise, tout
# caractere '.' lève FormatException ("Unknown char: ."). Parade robuste pour
# toute locale : AUCUN decimal dans l'OBJ (sommets ±1, normales 0/±1, UV 0/1) ;
# la mise a l'echelle reelle (x0.25 -> cube 0.5) se fait dans le XML du bloc
# via <Mesh><Scale>, parse par XmlSerializer (invariant, lui).
S = 1
lines = ["# Microcontroller block - cube x1 (scaled to 0.5 via block XML Scale)", "o Microcontroller"]
faces = [
    ([(-S,-S, S),( S,-S, S),( S, S, S),(-S, S, S)], (0,0,1)),    # +Z
    ([( S,-S,-S),(-S,-S,-S),(-S, S,-S),( S, S,-S)], (0,0,-1)),   # -Z
    ([( S,-S, S),( S,-S,-S),( S, S,-S),( S, S, S)], (1,0,0)),    # +X
    ([(-S,-S,-S),(-S,-S, S),(-S, S, S),(-S, S,-S)], (-1,0,0)),   # -X
    ([(-S, S, S),( S, S, S),( S, S,-S),(-S, S,-S)], (0,1,0)),    # +Y
    ([(-S,-S,-S),( S,-S,-S),( S,-S, S),(-S,-S, S)], (0,-1,0)),   # -Y
]
vi, ti, ni = 0, 0, 0
for verts, normal in faces:
    for v in verts:
        lines.append(f"v {v[0]} {v[1]} {v[2]}")
    for uv in [(0,0),(1,0),(1,1),(0,1)]:
        lines.append(f"vt {uv[0]} {uv[1]}")
    lines.append(f"vn {normal[0]} {normal[1]} {normal[2]}")
    vi += 4; ti += 4; ni += 1
    lines.append(f"f {vi-3}/{ti-3}/{ni} {vi-2}/{ti-2}/{ni} {vi-1}/{ti-1}/{ni} {vi}/{ti}/{ni}")
with open("microcontroller.obj", "w") as f:
    f.write("\n".join(lines) + "\n")
print("OBJ ecrit:", len(lines), "lignes")

# ---------------- PNG : texture 32x32 ----------------
W = H = 32
DARK  = (30, 34, 44, 255)     # corps PCB
LIGHT = (52, 60, 76, 255)     # substrat central
DIE   = (120, 200, 255, 255)  # die
GOLD  = (212, 168, 60, 255)   # broches
px = [DARK] * (W * H)

def put(x, y, c):
    px[y * W + x] = c

for y in range(6, 26):        # substrat central
    for x in range(6, 26):
        put(x, y, LIGHT)
for y in range(14, 18):      # die
    for x in range(14, 18):
        put(x, y, DIE)
for i in range(8, 24, 4):     # broches sur les 4 cotes
    for k in range(2):
        put(i + k, 1, GOLD); put(i + k, 30, GOLD)
        put(1, i + k, GOLD); put(30, i + k, GOLD)

raw = b""
for y in range(H):
    flat = [c for pixel in px[y * W:(y + 1) * W] for c in pixel]
    row = struct.pack("4B" * W, *flat)
    raw += b"\x00" + row     # filtre 0 par scanline

def chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff))

png = (b"\x89PNG\r\n\x1a\n"
       + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0))
       + chunk(b"IDAT", zlib.compress(raw))
       + chunk(b"IEND", b""))
with open("microcontroller.png", "wb") as f:
    f.write(png)
print("PNG ecrit:", len(png), "octets")
