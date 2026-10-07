"""Prepare bundled fonts from the official downloads in artifacts/font-sources.

Optional asset preparation, not required for normal builds. Requires fontTools.
"""
from pathlib import Path
from zipfile import ZipFile
import sys

root = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(root / '.tools/fonttools'))
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

sources = root / 'artifacts/font-sources'
output = root / 'src/Nook.App/Assets/Fonts'
output.mkdir(parents=True, exist_ok=True)
with ZipFile(sources / 'Pretendard-1.3.9.zip') as archive:
    for weight in ('Regular', 'Medium', 'SemiBold'):
        (output / f'Pretendard-{weight}.otf').write_bytes(archive.read(f'public/static/Pretendard-{weight}.otf'))
    (output / 'Pretendard-LICENSE.txt').write_bytes(archive.read('LICENSE.txt'))

for weight, style in ((400, 'Regular'), (600, 'SemiBold'), (800, 'ExtraBold')):
    variable = TTFont(sources / 'NunitoSans-variable.ttf')
    axes = {axis.axisTag: axis.defaultValue for axis in variable['fvar'].axes}
    axes['wght'] = weight
    font = instantiateVariableFont(variable, axes, inplace=False)
    # Fully static instances do not need variable-axis style names in DirectWrite.
    if 'STAT' in font:
        del font['STAT']
    for name_id, value in {1:'Nunito Sans', 2:style, 4:f'Nunito Sans {style}', 6:f'NunitoSans-{style}', 16:'Nunito Sans', 17:style, 21:'Nunito Sans', 22:style}.items():
        font['name'].setName(value, name_id, 3, 1, 0x409)
        font['name'].setName(value, name_id, 1, 0, 0)
    font['OS/2'].usWeightClass = weight
    font['OS/2'].fsSelection &= ~(0x20 | 0x40)
    font['head'].macStyle &= ~1
    if weight == 400:
        font['OS/2'].fsSelection |= 0x40
    if weight >= 700:
        font['OS/2'].fsSelection |= 0x20
        font['head'].macStyle |= 1
    font.save(output / f'NunitoSans-{style}.ttf')
(output / 'NunitoSans-OFL.txt').write_bytes((sources / 'NunitoSans-OFL.txt').read_bytes())
print(f'Bundled fonts: {output}')
