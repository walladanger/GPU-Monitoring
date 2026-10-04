import csv
import io
import json
import subprocess
import threading
import time
import webbrowser
from collections import deque
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

samples = deque(maxlen=900)
lock = threading.Lock()
error = None

def collect():
    global error
    while True:
        try:
            result = subprocess.run(['nvidia-smi', '--query-gpu=index,name,uuid,utilization.gpu,memory.used,memory.total,temperature.gpu,power.draw', '--format=csv,noheader,nounits'], capture_output=True, text=True, timeout=8, creationflags=subprocess.CREATE_NO_WINDOW)
            if result.returncode:
                raise RuntimeError(result.stderr.strip() or 'nvidia-smi failed')
            gpus = []
            for row in csv.reader(io.StringIO(result.stdout), skipinitialspace=True):
                if len(row) != 8:
                    continue
                def number(value):
                    try: return float(value)
                    except ValueError: return None
                gpus.append(dict(index=row[0], name=row[1], uuid=row[2], utilization=number(row[3]), memory=number(row[4]), total=number(row[5]), temperature=number(row[6]), power=number(row[7])))
            if not gpus: raise RuntimeError('No GPUs returned by nvidia-smi')
            with lock:
                samples.append(dict(time=time.time(), gpus=gpus))
                error = None
        except Exception as exc:
            with lock: error = str(exc)
        time.sleep(2)

PAGE = r'''<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>GPU Monitor</title><style>
*{box-sizing:border-box}body{margin:0;background:#0b1018;color:#eaf0f8;font:15px system-ui}main{max-width:1400px;margin:auto;padding:32px}header{display:flex;justify-content:space-between;align-items:center;margin-bottom:24px}h1{margin:0;font-size:28px}p,.muted{color:#94a3b8}#status{color:#79e2bd}#cards,.charts{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:18px}.card,.chart{background:#141e2b;border:1px solid #273448;border-radius:14px;padding:22px}.card h2{font-size:18px;margin:0 0 18px}.stats{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}.value{font-size:23px;font-weight:650;margin-top:8px}.label{font-size:12px;color:#a6b4c8}.charts{margin-top:20px}.chart h2{font-size:16px;margin:0 0 10px}canvas{width:100%;height:220px;display:block}footer{color:#94a3b8;margin-top:20px;font-size:13px}.legend{margin-top:10px}select{background:#141e2b;color:white;border:1px solid #3b4a60;border-radius:6px;padding:8px}#error{color:#ffb4a9;margin:16px 0;white-space:pre-wrap}@media(max-width:850px){#cards,.charts{grid-template-columns:1fr}.stats{grid-template-columns:repeat(2,1fr)}main{padding:18px}header{align-items:flex-start;gap:10px}}
</style></head><body><main><header><div><h1>GPU Monitor</h1><p>Live telemetry · refreshes every 2 seconds</p></div><div><div id="status">Connecting…</div><p><select id="window"><option value="300">Last 5 minutes</option><option value="900">Last 15 minutes</option><option value="1800">Last 30 minutes</option></select></p></div></header><div id="error"></div><div id="cards"></div><div class="charts"><section class="chart"><h2>GPU utilization · %</h2><canvas id="utilization"></canvas></section><section class="chart"><h2>VRAM used · GiB</h2><canvas id="memory"></canvas></section><section class="chart"><h2>Core temperature · °C</h2><canvas id="temperature"></canvas></section><section class="chart"><h2>Power draw · W</h2><canvas id="power"></canvas></section></div><div class="legend" id="legend"></div><footer>Local monitoring only. History stays in memory for 30 minutes and resets when the server stops. Temperature is the GPU core reading; memory junction and hotspot are not included.</footer></main><script>
const colors=['#55d9c1','#9b9bff','#ffba70','#fa8ac4'];let data=[];let gpuList=[];
const fmt=(n,d=0)=>n==null?'N/A':n.toFixed(d);
function graph(key){const c=document.getElementById(key),r=c.getBoundingClientRect(),dpr=devicePixelRatio||1;c.width=r.width*dpr;c.height=r.height*dpr;const ctx=c.getContext('2d');ctx.scale(dpr,dpr);const w=r.width,h=r.height,L=48,R=12,T=14,B=28;const now=Date.now()/1000,span=+document.getElementById('window').value,start=now-span;const points=data.filter(s=>s.time>=start);let max=key==='utilization'?100:key==='memory'?Math.max(1,...gpuList.map(g=>(g.total||0)/1024)):key==='temperature'?100:Math.max(100,...points.flatMap(s=>s.gpus.map(g=>g.power||0)))*1.15;const val=g=>key==='memory'?g.memory==null?null:g.memory/1024:g[key];ctx.font='11px system-ui';for(let i=0;i<=4;i++){let y=T+(h-T-B)*i/4;ctx.strokeStyle='#29364a';ctx.beginPath();ctx.moveTo(L,y);ctx.lineTo(w-R,y);ctx.stroke();ctx.fillStyle='#94a3b8';ctx.fillText(fmt(max*(1-i/4),key==='memory'?1:0),3,y+4)}gpuList.forEach((gpu,i)=>{ctx.strokeStyle=colors[i%colors.length];ctx.lineWidth=2;ctx.beginPath();let drawing=false;points.forEach(s=>{let g=s.gpus.find(g=>g.uuid===gpu.uuid),v=g?val(g):null;if(v==null){drawing=false;return}let x=L+(s.time-start)/span*(w-L-R),y=T+(1-v/max)*(h-T-B);if(drawing)ctx.lineTo(x,y);else ctx.moveTo(x,y);drawing=true});ctx.stroke()});ctx.fillStyle='#94a3b8';ctx.fillText(new Date(start*1000).toLocaleTimeString(),L,h-4);ctx.textAlign='right';ctx.fillText('Now',w-R,h-4)}
function draw(){['utilization','memory','temperature','power'].forEach(graph)}
async function refresh(){try{const res=await fetch('/api',{cache:'no-store'});const body=await res.json();data=body.samples;document.getElementById('error').textContent=body.error||'';const latest=data.at(-1);if(latest){gpuList=latest.gpus;const age=Date.now()/1000-latest.time;document.getElementById('status').textContent=body.error||age>10?'Readings stale':'Live · '+new Date(latest.time*1000).toLocaleTimeString();const cards=document.getElementById('cards');cards.replaceChildren();gpuList.forEach((g,i)=>{let card=document.createElement('section');card.className='card';let title=document.createElement('h2');title.style.color=colors[i%colors.length];title.textContent='GPU '+g.index+' · '+g.name;card.append(title);let stats=document.createElement('div');stats.className='stats';[['Utilization',fmt(g.utilization)+' %'],['VRAM',fmt(g.memory==null?null:g.memory/1024,1)+' / '+fmt(g.total==null?null:g.total/1024,0)+' GiB'],['Core temperature',fmt(g.temperature)+' °C'],['Power',fmt(g.power,1)+' W']].forEach(([label,value])=>{let el=document.createElement('div');let l=document.createElement('div');l.className='label';l.textContent=label;let v=document.createElement('div');v.className='value';v.textContent=value;el.append(l,v);stats.append(el)});card.append(stats);cards.append(card)});document.getElementById('legend').textContent=gpuList.map(g=>'GPU '+g.index+' · '+g.uuid).join('   |   ');draw()}else document.getElementById('status').textContent='Waiting for GPU readings'}catch(e){document.getElementById('status').textContent='Disconnected';document.getElementById('error').textContent='Could not reach the local monitoring server.'}}
document.getElementById('window').onchange=draw;window.onresize=draw;refresh();setInterval(refresh,2000);
</script></body></html>'''

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == '/api':
            with lock: payload = json.dumps(dict(samples=list(samples), error=error)).encode()
            content_type = 'application/json'
        elif self.path == '/':
            payload = PAGE.encode()
            content_type = 'text/html; charset=utf-8'
        else:
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header('Content-Type', content_type)
        self.send_header('Cache-Control', 'no-store')
        self.send_header('Content-Length', str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)
    def log_message(self, *args): pass

if __name__ == '__main__':
    server = ThreadingHTTPServer(('127.0.0.1', 8765), Handler)
    threading.Thread(target=collect, daemon=True).start()
    print('GPU dashboard: http://127.0.0.1:8765 — Ctrl+C to stop', flush=True)
    webbrowser.open('http://127.0.0.1:8765')
    try: server.serve_forever()
    except KeyboardInterrupt: pass
    finally: server.server_close()
