using System.Text;
using System.Text.Json;
using DesktopPet.App;
using DesktopPet.Core;

internal static class WalkPreviewCapture
{
    public static void Save(string charactersRoot, string destination)
    {
        var names = new[] { "Companion", "Nimbus", "Ember", "치이카와", "세균맨", "헬로키티" }
            .Where(name => Directory.Exists(Path.Combine(charactersRoot, name)));
        var sprites = new List<string>();
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tracks = new List<object>();
        int Sprite(string path)
        {
            if (indices.TryGetValue(path, out var existing)) return existing;
            var index = sprites.Count;
            indices.Add(path, index);
            sprites.Add("data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path)));
            return index;
        }
        foreach (var name in names)
        {
            var character = CharacterPresentation.Load(Path.Combine(charactersRoot, name)).Definition;
            var appearance = new WalkAppearanceController(new Random(7));
            var steps = new List<object>();
            var brain = new PetBrain(new PlatformLayout(new[] { new Platform(20, 620, 182) }),
                128, character.AnchorY, 250, 182 - character.AnchorY, new Random(42))
            { WalkSpeedPxPerSec = character.WalkSpeedPxPerSec, HopBetweenPlatforms = false, SleepEnabled = false };
            var idleMs = 0d;
            const double dt = 1d / 60;
            for (var i = 0; i < 960; i++)
            {
                var before = brain.X;
                brain.Tick(dt, new PetPoint(-10000, -10000));
                var state = brain.State;
                var moving = state == PetState.Walk;
                var right = brain.FacingRight;
                appearance.Update(state, right, brain.HorizontalVelocityPxPerSec, character.WalkSpeedPxPerSec, dt,
                    brain.WalkSpeedPxPerSec, brain.X - before, character.WalkCycleDistancePx);
                var displayWalk = appearance.DisplayWalkPose;
                var frames = displayWalk ? character.GetWalkFrames(appearance.View) : character.GetFrames(state);
                if (state == PetState.Idle && !displayWalk) idleMs += dt * 1000;
                var elapsed = displayWalk ? appearance.AnimationElapsedMs : state == PetState.Idle ? idleMs : brain.StateElapsedMs;
                var live = displayWalk && character.WalkRig is not null;
                var path = displayWalk && character.WalkCycleDistancePx is not null
                    ? character.GetWalkFramePath(appearance.View, appearance.GaitPhase, appearance.IsSettling ? appearance.SettleProgress : 0)
                    : character.GetFramePath(displayWalk ? PetState.Walk : state, frames, elapsed);
                var sprite = live ? -1 : Sprite(path);
                var body = -1; var near = -1; var far = -1;
                var pose = default(GroundedGaitPose);
                if (live)
                {
                    var rig = character.WalkRig!;
                    var view = rig.GetView(appearance.View);
                    body = Sprite(view.BodyPath); near = Sprite(view.NearFootPath); far = Sprite(view.FarFootPath);
                    pose = GroundedGait.Sample(appearance.GaitPhase, character.WalkCycleDistancePx!.Value, rig.FootLiftPx);
                    if (appearance.IsSettling) pose = GroundedGait.Settle(pose, appearance.SettleProgress);
                }
                steps.Add(new
                {
                    sprite, body, near, far, live, x = brain.X, right, moving, settling = appearance.IsSettling,
                    nearX = pose.FootA.X, nearY = -pose.FootA.Lift,
                    farX = pose.FootB.X, farY = -pose.FootB.Lift, bodyY = pose.BodyBob,
                    mirror = displayWalk ? !appearance.FacingRight : !character.UsesFrontalRestFrames(state) && !right,
                });
            }
            tracks.Add(new { name, frames = steps });
        }
        var html = $$"""
            <!doctype html><html lang="ko">
            <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>DesktopPet · 발을 딛는 보행 미리보기</title>
            <style>
            *{box-sizing:border-box}body{margin:0;background:#f4f7f1;color:#354e3b;font:15px "Segoe UI","Malgun Gothic",sans-serif;min-height:100vh;display:grid;place-items:center}
            main{width:min(760px,94vw);padding:28px;background:#fff;border:1px solid #dde7d9;border-radius:20px}
            h1{font-size:22px;margin:0 0 10px}p{color:#6d7c70;line-height:1.65;margin:0 0 20px}
            canvas{width:100%;height:auto;background:#f4f7f1;border-radius:12px;border:1px solid #e1e8dc}
            footer{display:flex;gap:10px;align-items:center;margin-top:18px;flex-wrap:wrap}
            button,select{font:inherit;border:1px solid #c8d6c4;border-radius:8px;padding:8px 14px;background:#edf4e9;color:#354e3b}
            button{cursor:pointer}span{font-size:12px;color:#6d7c70;margin-left:auto}
            </style><main>
            <h1>바닥을 딛고, 한 걸음씩</h1>
            <p>실제 앱의 이동·가감속과 연속 보행을 기록했습니다. 캐시한 몸통·양발을 각각 그려 저속 이동과 멈출 때의 발 내리기를 확인할 수 있습니다.</p>
            <canvas id="scene" width="640" height="210" aria-label="캐릭터가 옆으로 걷다가 멈추어 앞을 보는 반복 애니메이션"></canvas>
            <footer><select id="skin" aria-label="캐릭터 선택"></select><button id="play" type="button">일시정지</button><button id="reset" type="button">처음부터</button><span>60fps 기록 · 16초 반복 · 오프라인</span></footer>
            </main><script>
            'use strict';
            const sprites={{JsonSerializer.Serialize(sprites)}}, tracks={{JsonSerializer.Serialize(tracks)}};
            const canvas=document.getElementById('scene'), context=canvas.getContext('2d');
            const play=document.getElementById('play'), skin=document.getElementById('skin');
            tracks.forEach((track,index)=>{const option=document.createElement('option');option.value=index;option.textContent=track.name;skin.appendChild(option)});
            let paused=window.matchMedia('(prefers-reduced-motion: reduce)').matches, elapsed=0,last=0,ready=false;
            const images=sprites.map(src=>{const image=new Image();image.src=src;return image});
            function paint(){
              if(!ready)return;
              const frames=tracks[Number(skin.value)].frames,step=frames[Math.floor(elapsed/(1000/60))%frames.length];
              context.clearRect(0,0,640,210);context.strokeStyle='#cad7c7';context.beginPath();context.moveTo(20,182);context.lineTo(620,182);context.stroke();
              context.fillStyle='#354e3b';context.font='17px "Segoe UI","Malgun Gothic",sans-serif';
              context.fillText(step.settling?'발을 내려 멈추기':step.moving?(step.right?'오른쪽으로 걷기':'왼쪽으로 걷기'):'쉬는 중',22,32);
              context.save();context.translate(step.x,60);if(step.mirror){context.translate(128,0);context.scale(-1,1)}
              if(step.live){
                context.drawImage(images[step.far],step.farX,step.farY,128,128);
                context.drawImage(images[step.near],step.nearX,step.nearY,128,128);
                context.drawImage(images[step.body],0,step.bodyY,128,128);
              }else context.drawImage(images[step.sprite],0,0,128,128);
              context.restore();
            }
            function label(){play.textContent=paused?'재생':'일시정지'}
            play.onclick=()=>{paused=!paused;label()};
            skin.onchange=()=>{elapsed=0;paint()};
            document.getElementById('reset').onclick=()=>{elapsed=0;paint()};
            Promise.all(images.map(image=>image.decode())).then(()=>{
              ready=true;label();paint();requestAnimationFrame(function tick(now){if(last&&!paused)elapsed+=(now-last);last=now;paint();requestAnimationFrame(tick)});
            }).catch(()=>{context.fillText('이미지를 읽지 못했습니다. 파일을 다시 열어주세요.',22,60)});
            </script></html>
            """;
        File.WriteAllText(Path.Combine(destination, "character-walk-v8.html"), html, new UTF8Encoding(false));
    }
}
