"""Create a single readable animation overview and face before/after study."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art/corgi-image-to-3d/rigged/previews'
font=lambda size:ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',size)
labels=[('Idle','待机'),('Blink','闭眼'),('Pet','摸摸'),('Eat','咬食'),('PlayBall','拨球'),('Sleep','趴睡'),('Wake','起身')]


def gif_frames(name,label):
    result=[]
    for path in sorted((OUT/'frames'/name).glob('*.png')):
        image=Image.open(path).convert('RGBA')
        frame=Image.new('RGB',image.size,(239,242,245));frame.paste(image,(0,0),image)
        ImageDraw.Draw(frame).text((12,10),label,font=font(19),fill=(50,58,67))
        result.append(frame)
    return result


def save_gif(frames,path):
    # Alternate centisecond delays instead of truncating every 12-fps frame.
    delays=[round((index+1)*1000/12/10)*10-round(index*1000/12/10)*10 for index in range(len(frames))]
    frames[0].save(path,save_all=True,append_images=frames[1:],duration=delays,loop=0,disposal=2)


demo=[]
for name,label in labels:
    frames=gif_frames(name,label)
    if frames:
        save_gif(frames,OUT/(name+'.gif'))
        if name in ('Pet','Eat','PlayBall','Sleep','Wake'):demo.extend(frames)
if demo:save_gif(demo,OUT/'interaction-demo.gif')
sheet=Image.new('RGB',(1260,1010),(239,242,245))
draw=ImageDraw.Draw(sheet)
draw.text((26,18),'柯基 · 骨骼与互动动作',font=font(30),fill=(36,45,54))
draw.text((26,67),'24 个骨骼 · 独立眼睑与口腔 · 真实三维蒙皮和面部变形 · 可编辑 GLB',font=font(18),fill=(85,95,106))
for index,(name,label) in enumerate(labels[:6]):
    x=20+(index%3)*415;y=108+(index//3)*370
    draw.rounded_rectangle((x,y,x+395,y+355),radius=12,fill=(251,251,249))
    draw.text((x+15,y+12),label,font=font(21),fill=(45,55,65))
    image=Image.open(OUT/(name+'-peak.png')).convert('RGBA')
    box=image.getchannel('A').getbbox();image=image.crop(box)
    image.thumbnail((365,295),Image.Resampling.LANCZOS)
    sheet.paste(image,(x+15+(365-image.width)//2,y+51+(295-image.height)//2),image)
draw.text((28,874),'包含起身动作：从趴睡末帧平滑回到坐姿；食物和球带接触锚点。',font=font(20),fill=(68,78,89))
draw.text((28,916),'此页为实际 GLB 动作峰值。播放完整 GIF 查看过渡和恢复。',font=font(18),fill=(90,100,111))
draw.text((28,953),'当前为可编辑动作初版，尚未接入桌宠。',font=font(18),fill=(90,100,111))
sheet.save(OUT/'action-overview.jpg',quality=93)
face=Image.new('RGB',(1260,510),(239,242,245));draw=ImageDraw.Draw(face)
for index,(name,label) in enumerate([('face-open','睁眼'),('face-closed','闭眼'),('mouth-open','张嘴咬食')]):
    image=Image.open(OUT/(name+'.png')).convert('RGBA');image.thumbnail((400,440))
    face.paste(image,(index*420+10,54),image)
    draw.text((index*420+20,14),label,font=font(23),fill=(45,55,65))
face.save(OUT/'face-study.jpg',quality=93)
print(OUT/'action-overview.jpg')
