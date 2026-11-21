from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import urllib.request
root=Path('work/Symbole/assets')
base='https://raw.githubusercontent.com/Templarian/MaterialDesign-Webfont/v7.4.47/'
def fetch(pair):
 remote,local=pair
 data=urllib.request.urlopen(base+remote,timeout=60).read()
 (root/local).write_bytes(data)
 return local,len(data)
with ThreadPoolExecutor(max_workers=3) as pool:
 for result in pool.map(fetch,[('fonts/materialdesignicons-webfont.ttf','mdi.ttf'),('css/materialdesignicons.css','mdi.css'),('LICENSE','LICENSE-MDI.txt')]): print(result)
