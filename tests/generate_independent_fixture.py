from pathlib import Path
import json, zipfile, html
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.colors import black, white
from reportlab.lib.utils import ImageReader
from PIL import Image, ImageDraw, ImageFont

root=Path(__file__).resolve().parent/'.runs'/'independent-audit';root.mkdir(parents=True,exist_ok=True)
fontpath='C:/Windows/Fonts/arial.ttf'
pdfmetrics.registerFont(TTFont('ArialTest',fontpath))
font=ImageFont.truetype(fontpath,28);small=ImageFont.truetype(fontpath,23)
pdf=canvas.Canvas(str(root/'independent.pdf'),pagesize=(595,842))
gold={'Name':'Независимые искусственные документы: чёрные подписи, пустая ячейка, отсутствующая печать, несовпадения названий и фамилий','KnownFindings':[], 'NegativeChecks':[], 'Fields':[]}
images=[]
for doc in range(2):
    code=f'123-45-6789-ИЛО4.1.{doc+1}'
    for page in range(4):
        im=Image.new('RGB',(1600,2264),'white');d=ImageDraw.Draw(im)
        def text(x,y,s,f=font):d.text((x,y),s,font=f,fill='black')
        text(70,70,'ЛЕНГИПРОТРАНС — ИСКУССТВЕННЫЙ ТЕСТ')
        if page==0:
            text(100,400,'ПРОЕКТНАЯ ДОКУМЕНТАЦИЯ')
            text(100,540,'Раздел 4. Здания, строения и сооружения')
            text(100,610,'Подраздел 4. Инженерное оборудование')
            text(100,680,'Часть 1. Система электроснабжения')
            text(100,760,'Книга '+str(doc+1)+'. Контрольное здание')
            text(100,900,code);text(100,990,f'Том 4.4.1.{doc+1}')
            for row,(role,name) in enumerate([('Главный инженер','Александров'),('Главный инженер проекта','Иванов')]):
                y=1720+row*170;text(50,y,role,small);text(1200,y,name,small)
                d.line([(620,y+65),(650,y-40),(740,y+70),(700,y-10),(810,y+10)],fill='black',width=5)
            if doc==0:
                d.ellipse((430,1620,760,1950),outline='black',width=5)
                d.ellipse((440,1630,750,1940),outline='black',width=3)
        else:
            text(60,220,'Информационно-удостоверяющий лист')
            text(60,310,code);text(550,370,f'Том 4.4.1.{doc+1}')
            if page==1:
                text(430,580,'Раздел 4. Здания, строения и сооружения',small)
                text(430,650,'Подраздел 4. Инженерное оборудование',small)
                text(430,720,'Часть 1. Система электроснабжения',small)
                text(430,810,'Книга '+str(doc+1)+('. Контрольное здание' if doc==0 else '. Котельная'),small)
                text(1270,350,'Номер последнего');text(1300,400,'изменения');text(1380,510,'2')
                text(540,1120,'CRC32:');text(540,1190,'1234ABCD' if doc==0 else 'DEADBEEF')
            roles=[('Главный инженер','Александров'),('Главный инженер проекта','Иванов' if doc==0 else 'Петров')]
            for row,(role,name) in enumerate(roles):
                y=(1440 if page==1 else 450)+row*220
                d.rectangle((45,y-20,1550,y+190),outline='black',width=3)
                for x in (420,850,1310):d.line((x,y-20,x,y+190),fill='black',width=3)
                # Roles are split into lines as in a real IUL form.
                words=role.split();text(65,y,' '.join(words[:2]),small)
                if len(words)>2:text(65,y+42,' '.join(words[2:]),small)
                text(450,y+20,name,small);text(1330,y+20,'05.10.2026',small)
                if not(doc==1 and page==2 and row==1):
                    d.line([(900,y+110),(950,y+30),(990,y+120),(1020,y+40),(1170,y+75)],fill='black',width=5)
            text(1350,2010,'Лист');text(1455,2010,'Листов');text(1380,2090,str(page));text(1490,2090,'3')
            gold['Fields'] += [{'Photo':doc*4+page+1,'Field':'Page','Value':str(page)}, {'Photo':doc*4+page+1,'Field':'Pages','Value':'3'}]
            if page==1:gold['Fields'] += [{'Photo':doc*4+page+1,'Field':'CRC','Value':'1234ABCD' if doc==0 else 'DEADBEEF'}, {'Photo':doc*4+page+1,'Field':'Revision','Value':'2'}]
        text(80,2180,'Все имена и данные вымышлены. Не реальная документация.',small)
        path=root/f'page-{doc*4+page+1:04}.png';im.save(path);images.append(path)
        pdf.drawImage(ImageReader(im),0,0,width=595,height=842);pdf.showPage()
pdf.save()
gold['KnownFindings']=[{'Code':'123-45-6789-ИЛО4.1.2','Topic':'Название книги в ИУЛ'}, {'Code':'123-45-6789-ИЛО4.1.2','Topic':'Главный инженер проекта: титул / ИУЛ'},
    {'Code':'123-45-6789-ИЛО4.1.2','Photo':5,'Topics':['Печать / подписи на титуле','Печать на титуле']},
    {'Code':'123-45-6789-ИЛО4.1.2','Photo':7,'Topics':['Подписи в ИУЛ: визуальная проверка','Подписи: проверка отдельных строк']}]
gold['NegativeChecks']=[{'Code':'123-45-6789-ИЛО4.1.1','Topic':'Не подтверждена комплектность ИУЛ'}, {'Code':'123-45-6789-ИЛО4.1.1','Topic':'Название книги в ИУЛ'}]
gold['NegativeChecks'] += [{'Code':'123-45-6789-ИЛО4.1.1','Photo':1,'Topics':['Печать / подписи на титуле','Печать на титуле']},
    {'Code':'123-45-6789-ИЛО4.1.1','Photo':1,'Topics':['Печать / подписи на титуле','Подписи: проверка отдельных строк']}]
for photo in [2,3,4,6,8]:gold['NegativeChecks'].append({'Code':f'123-45-6789-ИЛО4.1.{1 if photo<5 else 2}','Photo':photo,'Topics':['Подписи в ИУЛ: визуальная проверка','Подписи: проверка отдельных строк']})
(root/'reference.json').write_text(json.dumps(gold,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'pdf':str(root/'independent.pdf'),'pages':8,'fields':len(gold['Fields'])}))
