"""Thin desktop UI over the same tested CLI workflow."""
import json
import os
from pathlib import Path
import queue
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
import webbrowser
import workflow as w

def launch(initial_case=None):
    root=tk.Tk();root.title('HB 設備圖資工作台 · v0.2 空調箱開發版');root.geometry('1120x820');root.minsize(980,720)
    style=ttk.Style();style.theme_use('clam');style.configure('TLabel',font=('Microsoft JhengHei UI',11));style.configure('TButton',font=('Microsoft JhengHei UI',10),padding=8)
    frame=ttk.Frame(root,padding=22);frame.pack(fill='both',expand=True)
    ttk.Label(frame,text='設備圖資 → 覆核 → Revit 族群',font=('Microsoft JhengHei UI',20,'bold')).pack(anchor='w')
    ttk.Label(frame,text='全程本機處理。支援圖資匯入不等於已具備各種設備的建族範本。').pack(anchor='w',pady=(5,15))
    state={'files':[],'case':None,'request':None,'busy':False,'pending':None};events=queue.Queue()
    category=tk.StringVar(value='pump');target=tk.StringVar(value='2024');status=tk.StringVar(value='請選擇設備與圖資。')
    category_labels={label:key for key,label in w.CATEGORIES.items()}
    row=ttk.Frame(frame);row.pack(fill='x')
    ttk.Label(row,text='設備分類').pack(side='left');combo=ttk.Combobox(row,state='readonly',values=list(category_labels),width=25);combo.current(0);combo.pack(side='left',padx=10)
    ttk.Label(row,text='交付版本').pack(side='left');ttk.Combobox(row,state='readonly',textvariable=target,values=['2024','2025','2026'],width=8).pack(side='left',padx=10)
    ttk.Label(frame,text='CAD：DWG／DXF（2D、3D）  ·  PDF：文字／掃描  ·  圖片：PNG、JPG、TIFF 等\n可建族：TOS-EF-05／21 泵浦、東元 PJ0043-HS／PJ0063-HS 空調箱（Revit 2024）。',wraplength=1020).pack(anchor='w',pady=12)
    files= tk.Listbox(frame,height=4,font=('Microsoft JhengHei UI',10));files.pack(fill='x')
    bar=ttk.Frame(frame);bar.pack(fill='x',pady=8)
    buttons=[]
    def log(text):
        output.configure(state='normal');output.insert('end',str(text)+'\n');output.see('end');output.configure(state='disabled')
    def show_case():
        if not state['case']:return
        m=w.read(state['case']/'case.json');result=w.validate(state['case'])
        files.delete(0,'end')
        for source in m['sources']:files.insert('end',source['original_name']+'  —  '+w.STATUS_LABELS.get(source['status'],source['status']))
        target.set(str(m['target_revit']))
        for label,key in category_labels.items():
            if key==m['category']:combo.set(label)
        for child in tree.get_children():tree.delete(child)
        is_ahu=m['category']=='ahu'
        for col,label in zip(tree['columns'], ['型號','總長 C','寬 D','總高 F','出風 G','出風 H'] if is_ahu else ['組合型號','來源 A','來源 D','來源 H','來源 H3','HP']):tree.heading(col,text=label)
        for t in (m.get('recipe') or {}).get('types',[]):
            if is_ahu:values=[t['model'],t['C'],t['D'],t['F'],t['G'],t['H']]
            else:
                d=t['assembly_dimensions'];values=[t['assembly_model'],d['A'],d['D'],d['H'],d['H3'],t['motor_hp']]
            tree.insert('', 'end',values=values)
        status.set(('可產生概念建族工作包' if result['can_build'] else '待補足／覆核')+' · '+str(state['case']))
        log('\n'.join(result['errors']+result['warnings']))
    def work(label,fn,done=None):
        if state['busy']:return
        state['busy']=True
        for b in buttons:b.configure(state='disabled')
        status.set(label)
        def thread():
            try:events.put(('ok',fn(),done))
            except Exception as exc:events.put(('error',str(exc),None))
        threading.Thread(target=thread,daemon=True).start()
    def poll():
        try:
            while True:
                kind,value,done=events.get_nowait();state['busy']=False
                for b in buttons:b.configure(state='normal')
                if kind=='error':status.set('處理未完成');log(value);messagebox.showerror('設備圖資工作台',value)
                else:
                    if done:done(value)
                    show_case()
        except queue.Empty:pass
        if state['pending']:
            folder=state['pending']
            if (folder/'result.json').is_file():
                result=w.read(folder/'result.json');state['pending']=None
                status.set('族群已建立；接續驗證：'+str(result.get('project_duct_connection',result.get('project_pipe_connection'))))
                log('成果資料夾：'+str(folder));w.report(state['case'])
            elif (folder/'failure.txt').is_file():
                state['pending']=None;status.set('建族未完成，請查看錯誤紀錄。');log((folder/'failure.txt').read_text(encoding='utf-8-sig'))
            else:status.set('Revit 建族執行中；若啟動被其他外掛提示阻擋，請先處理該提示。')
        root.after(150,poll)
    def button(parent,text,command):
        b=ttk.Button(parent,text=text,command=command);b.pack(side='left',padx=(0,8));buttons.append(b)
    def select():
        paths=filedialog.askopenfilenames(title='選取同一設備的圖面、型錄與圖片',filetypes=[('設備圖資','*.pdf *.dwg *.dxf *.png *.jpg *.jpeg *.bmp *.tif *.tiff *.webp *.sat *.step *.stp *.igs *.iges'),('所有檔案','*.*')])
        if paths:
            state['files']=list(paths);files.delete(0,'end')
            for p in paths:files.insert('end',p)
    def import_case():
        if not state['files']:messagebox.showinfo('尚未選取','請先選擇圖資。');return
        destination=filedialog.askdirectory(title='選擇工作資料夾的存放位置')
        if not destination:return
        case=Path(destination)/('equipment-'+w.uuid.uuid4().hex[:8])
        chosen=category_labels[combo.get()];version=int(target.get());sources=list(state['files'])
        def done(path):
            state.update(case=path,request=None);reviewer.set('');note.set('');accepted.set(False);log('已保留來源快照：'+str(path))
        work('解析圖資中；多頁 OCR 或 CAD 可能需要數分鐘…',lambda:w.new_case(chosen,sources,case,version),done)
    def open_case():
        p=filedialog.askopenfilename(title='開啟既有工作資料',filetypes=[('工作資料','case.json')])
        if p:
            state.update(case=Path(p).parent,request=None);show_case()
            existing=w.read(state['case']/'case.json').get('review') or {}
            reviewer.set(existing.get('reviewer',''));note.set(existing.get('note',''));accepted.set(existing.get('accept_concept',False))
    def need_case():
        if not state['case']:raise ValueError('請先建立或開啟工作資料夾')
        return state['case']
    def view_report():
        try:webbrowser.open((need_case()/'report.html').as_uri())
        except Exception as e:messagebox.showinfo('提示',str(e))
    def sample():
        try:
            case=need_case();kind=w.read(case/'case.json')['category']
            if kind not in ('pump','ahu'):raise ValueError('此分類尚無建族範本。')
            w.set_recipe(case,w.AHU_RECIPE if kind=='ahu' else w.RECIPE);state['request']=None
            accepted.set(False);reviewer.set('');note.set('');show_case()
            log('已依工作分類載入範本，請對照來源與報告內的假設，再儲存覆核。此步驟不是 OCR 自動建模。')
        except Exception as e:messagebox.showerror('範本',str(e))
    button(bar,'1 選擇圖資',select);button(bar,'2 建立工作資料並解析',import_case);button(bar,'開啟既有工作',open_case);button(bar,'查看圖資與解析報告',view_report)
    ttk.Label(frame,text='尺寸覆核（mm）',font=('Microsoft JhengHei UI',12,'bold')).pack(anchor='w',pady=(10,5))
    tree=ttk.Treeview(frame,columns=['model','A','D','H','H3','HP'],show='headings',height=3)
    for col,label in zip(['model','A','D','H','H3','HP'],['組合型號','來源 A','來源 D','來源 H','來源 H3','HP']):tree.heading(col,text=label);tree.column(col,width=130)
    tree.pack(fill='x')
    bar2=ttk.Frame(frame);bar2.pack(fill='x',pady=8);button(bar2,'3 套用此設備分類範本',sample)
    fields=ttk.Frame(frame);fields.pack(fill='x')
    reviewer=tk.StringVar();note=tk.StringVar();accepted=tk.BooleanVar(value=False)
    ttk.Label(fields,text='覆核者').grid(row=0,column=0,sticky='w');ttk.Entry(fields,textvariable=reviewer,width=18).grid(row=0,column=1,padx=8)
    ttk.Label(fields,text='尺寸來源／版次確認說明').grid(row=0,column=2);ttk.Entry(fields,textvariable=note,width=64).grid(row=0,column=3,padx=8,sticky='ew');fields.columnconfigure(3,weight=1)
    ttk.Checkbutton(frame,text='已對照來源及報告，接受此範本列出的概念幾何、接頭假設與缺漏資料。',variable=accepted).pack(anchor='w',pady=8)
    def approve():
        try:w.review(need_case(),reviewer.get(),note.get(),accepted.get());state['request']=None;show_case()
        except Exception as e:messagebox.showerror('覆核未完成',str(e))
    def packet():
        try:state['request']=w.prepare(need_case());log('工作包：'+str(state['request']));status.set('工作包已建立，可執行 Revit 2024。')
        except Exception as e:messagebox.showerror('無法產生',str(e))
    def run_revit():
        if state['pending']:messagebox.showinfo('執行中','目前已有一個建族工作執行中。');return
        if not state['request']:messagebox.showinfo('尚無工作包','請先完成覆核並產生工作包。');return
        request=state['request']
        ps=Path(os.environ.get('WINDIR',r'C:\Windows'))/'System32/WindowsPowerShell/v1.0/powershell.exe'
        def run():
            r=w.execute([ps,'-NoProfile','-ExecutionPolicy','Bypass','-File',w.HERE/'Run-Case.ps1','-RequestPath',request])
            if r.returncode:raise RuntimeError(r.stderr.decode('utf-8','replace'))
            return r.stdout.decode('utf-8','replace')
        def started(value):
            state['pending']=request.parent/'output'
            log('Revit 已啟動。請留意原有外掛提示；結果將寫入工作包 output。\n'+value)
        work('正在啟動 Revit…',run,started)
    def open_results():
        try:
            folders=sorted(need_case().glob('builds/*/output'),key=lambda p:p.stat().st_mtime,reverse=True)
            if not folders:raise ValueError('尚無建族成果資料夾')
            os.startfile(str(folders[0]))
        except Exception as exc:messagebox.showinfo('成果',str(exc))
    bar3=ttk.Frame(frame);bar3.pack(fill='x',pady=5)
    button(bar3,'4 儲存覆核',approve);button(bar3,'5 產生建族工作包',packet);button(bar3,'6 執行 Revit 2024',run_revit)
    button(bar3,'開啟成果資料夾',open_results)
    ttk.Label(frame,textvariable=status,wraplength=1030).pack(anchor='w',pady=8)
    output=tk.Text(frame,height=7,font=('Microsoft JhengHei UI',10),state='disabled',wrap='word');output.pack(fill='both',expand=True)
    def close():
        if state['busy']:messagebox.showinfo('處理中','請等目前解析完成再關閉，避免留下未完成的工作資料。');return
        root.destroy()
    if initial_case:
        state['case']=Path(initial_case).resolve();show_case()
        existing=w.read(state['case']/'case.json').get('review') or {}
        reviewer.set(existing.get('reviewer',''));note.set(existing.get('note',''));accepted.set(existing.get('accept_concept',False))
    root.protocol('WM_DELETE_WINDOW',close);root.after(150,poll);root.mainloop()

if __name__=='__main__':launch()
