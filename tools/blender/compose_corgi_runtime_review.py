"""Compose actual WPF production-view renders produced by BreedChecks."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'qa/breeds'
OUTPUT=ROOT/'docs/images/corgi-runtime-review.jpg'
sheet=Image.new('RGB',(1140,850),(239,242,245))
draw=ImageDraw.Draw(sheet)
font=lambda size:ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',size)
draw.text((24,16),'柯基 · 桌宠程序实际渲染',font=font(29),fill=(41,49,57))
for index,(name,label) in enumerate([('idle','待机'),('pet','摸摸'),('feed','咬食'),('play','拨球'),('sleep','入睡过程'),('wake','起身过程')]):
    x=18+(index%3)*375;y=73+(index//3)*370
    draw.rounded_rectangle((x,y,x+354,y+349),radius=12,fill=(251,251,249))
    draw.text((x+14,y+12),label,font=font(21),fill=(51,60,69))
    image=Image.open(SOURCE/('corgi-'+name+'.png')).convert('RGBA')
    image=image.crop(image.getchannel('A').getbbox())
    image.thumbnail((322,289),Image.Resampling.LANCZOS)
    sheet.paste(image,(x+16+(322-image.width)//2,y+48+(289-image.height)//2),image)
draw.text((24,818),'桌面与小窝使用同一套动画；右键或小窝内可切换模型。',font=font(18),fill=(91,101,111))
sheet.save(OUTPUT,quality=93)
print(OUTPUT)
