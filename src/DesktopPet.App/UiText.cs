using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace DesktopPet.App;

/// <summary>Small offline KO/EN/zh-CN UI catalog. Character names and user-entered text stay intact.</summary>
public static class UiText
{
    private static string _language = "ko";
    private static readonly DependencyProperty OriginalTextProperty = DependencyProperty.RegisterAttached(
        "OriginalText", typeof(string), typeof(UiText), new PropertyMetadata(null));
    private static readonly DependencyProperty OriginalTitleProperty = DependencyProperty.RegisterAttached(
        "OriginalTitle", typeof(string), typeof(UiText), new PropertyMetadata(null));
    public static string Language => _language;
    public static event Action? LanguageChanged;
    public static void SetLanguage(string preference)
    {
        if (preference is not ("auto" or "ko" or "en" or "zh-CN")) throw new ArgumentOutOfRangeException(nameof(preference));
        var system = CultureInfo.CurrentUICulture.Name;
        var language = preference == "auto" ? system.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? "ko" :
            system.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en" : preference;
        if (_language == language) return;
        _language = language;
        LanguageChanged?.Invoke();
    }
    public static string Get(string original) => !Translations.TryGetValue(original, out var text) ? original :
        _language == "en" ? text.English : _language == "zh-CN" ? text.Chinese : original;
    public static string Format(string original, params object[] values) => string.Format(CultureInfo.CurrentCulture, Get(original), values);
    public static void Attach(Window window)
    {
        void Refresh() => TranslateWindow(window);
        RoutedEventHandler loaded = (_, _) => Refresh();
        window.Loaded += loaded;
        LanguageChanged += Refresh;
        window.Closed += (_, _) => { LanguageChanged -= Refresh; window.Loaded -= loaded; };
        Refresh();
    }
    public static void TranslateWindow(Window window)
    {
        var original = (string?)window.GetValue(OriginalTitleProperty) ?? window.Title;
        window.SetValue(OriginalTitleProperty, original);
        window.Title = Get(original);
        Walk(window);
    }
    private static void Walk(DependencyObject node)
    {
        var text = node is TextBlock block ? block.Text : node is ContentControl control && control.Content is string value ? value : null;
        if (text is not null)
        {
            var original = (string?)node.GetValue(OriginalTextProperty) ?? text;
            node.SetValue(OriginalTextProperty, original);
            if (node is TextBlock target) target.Text = Get(original);
            else if (node is ContentControl targetControl) targetControl.Content = Get(original);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) Walk(child);
    }
    private static readonly Dictionary<string, (string English, string Chinese)> Translations = new(StringComparer.Ordinal)
    {
        ["알림"] = ("Notification", "通知"),
        ["클릭해서 확인"] = ("Click to acknowledge", "点击确认"),
        ["클릭해서 확인 · 대기 {0}건"] = ("Click to acknowledge · {0} pending", "点击确认 · 待处理 {0} 条"),
        ["친구들을 불러오고 있어요…"] = ("Loading companions…", "正在加载伙伴…"),
        ["캐릭터를 불러오고 있어요…"] = ("Loading character…", "正在加载角色…"),
        ["ZIP으로 내보내기"] = ("Export ZIP", "导出 ZIP"),
        ["내보내기 취소"] = ("Cancel export", "取消导出"),
        ["캐릭터 ZIP 저장"] = ("Save character ZIP", "保存角色 ZIP"),
        ["{0} ZIP을 내보내고 있어요…"] = ("Exporting {0} ZIP…", "正在导出 {0} ZIP…"),
        ["{0} ZIP 내보내는 중… {1}/{2}"] = ("Exporting {0} ZIP… {1}/{2}", "正在导出 {0} ZIP… {1}/{2}"),
        ["캐릭터 ZIP을 저장했어요."] = ("Character ZIP saved.", "角色 ZIP 已保存。"),
        ["ZIP 내보내기를 취소했어요."] = ("ZIP export cancelled.", "已取消 ZIP 导出。"),
        ["ZIP 내보내기를 취소하고 있어요…"] = ("Cancelling ZIP export…", "正在取消 ZIP 导出…"),
        ["ZIP을 저장하지 못했어요. 새 파일 이름과 캐릭터 그림을 확인해주세요."] = ("Could not save ZIP. Choose a new filename and check the artwork.", "无法保存 ZIP，请使用新文件名并检查角色图像。"),
        ["{0}종 중 {1}종"] = ("Showing {1} of {0} characters", "显示 {0} 个角色中的 {1} 个"),
        ["현재 친구: {0}"] = ("Current companion: {0}", "当前伙伴：{0}"),
        ["선택형 팬아트 팩"] = ("Optional fan-art pack", "可选同人角色包"),
        ["직접 추가한 친구"] = ("A companion you added", "你添加的伙伴"),
        ["DesktopPet 오리지널 친구"] = ("DesktopPet original companion", "DesktopPet 原创伙伴"),
        ["캐릭터 그림을 읽을 수 없어요."] = ("Cannot read the character artwork.", "无法读取角色图像。"),
        ["목록을 불러오지 못했어요."] = ("Cannot load the character list.", "无法加载角色列表。"),
        ["캐릭터 폴더를 확인하고 새로고침해주세요."] = ("Check the character folder and refresh.", "请检查角色文件夹后刷新。"),
        ["새로고침 (F5)"] = ("Refresh (F5)", "刷新 (F5)"),
        ["움직임을 읽지 못했어요. 캐릭터 파일을 확인해주세요."] = ("Cannot load the animation. Check the character files.", "无法读取动作，请检查角色文件。"),
        ["{0} 적용했어요."] = ("Applied {0}.", "已应用 {0}。"),
        ["적용하지 못했어요. 현재 친구는 그대로예요."] = ("Could not apply the character. Your current pet was kept.", "无法应用，当前宠物保持不变。"),
        ["DesktopPet 캐릭터 갤러리"] = ("DesktopPet Character Gallery", "DesktopPet 角色画廊"),
        ["오늘 함께할 친구"] = ("Today's companion", "今天的伙伴"),
        ["골라서 미리 보고, 마음에 들면 적용하세요."] = ("Choose a character, preview it, and apply your favorite.", "先选择并预览角色，再应用喜欢的伙伴。"),
        ["이름 검색 · 한글 / English"] = ("Search names · Korean / English", "搜索名称 · 韩文 / 英文"),
        ["검색 결과가 없어요."] = ("No characters match your search.", "没有匹配的角色。"),
        ["움직임 미리보기"] = ("Animation preview", "动作预览"),
        ["이 친구와 함께하기"] = ("Choose this companion", "选择这个伙伴"),
        ["설치 취소"] = ("Cancel installation", "取消安装"),
        ["압축 해제"] = ("Extracting", "解压"), ["파일 복사"] = ("Copying", "复制"), ["PNG 검사"] = ("Validating PNGs", "验证 PNG"), ["준비 중"] = ("Preparing", "准备中"),
        ["설치는 이미 완료됐어요. 현재 친구는 바꾸지 않았습니다."] = ("Installation already finished. Your current pet was kept.", "安装已完成，当前宠物保持不变。"),
        ["취소 요청 중… 임시 파일을 정리합니다."] = ("Cancelling… Cleaning temporary files.", "正在取消并清理临时文件…"),
        ["펫 위치 복구"] = ("Recover pet position", "恢复宠物位置"),
        ["선택한 화면으로 데려오기"] = ("Bring to selected monitor", "移到所选屏幕"),
        ["펫 데려오기"] = ("Bring pet here", "将宠物带到这里"),
        ["선택한 화면 바닥으로 이동합니다. 트레이의 펫 데려오기는 마우스가 있는 화면을 사용해요."] = ("Move to the selected monitor's floor. The tray action uses the monitor nearest your pointer.", "移到所选屏幕底部；托盘操作使用鼠标所在或最近的屏幕。"),
        ["화면 구성이 바뀌었어요. 목록에서 다시 선택해주세요."] = ("Displays changed. Choose from the refreshed list.", "屏幕配置已变，请从刷新后的列表重选。"),
        ["선택한 화면으로 데려왔어요. 알림과 집중 시간은 유지합니다."] = ("Pet recovered. Notifications and focus time are preserved.", "已恢复位置，保留通知与专注时间。"),
        ["진단 보고서"] = ("Diagnostic report", "诊断报告"),
        ["DesktopPet 진단 보고서"] = ("DesktopPet diagnostic report", "DesktopPet 诊断报告"),
        ["릴리스 페이지"] = ("Release page", "版本发布"),
        ["업데이트는 공식 릴리스에서 직접 받습니다. 자동 다운로드나 실행은 하지 않아요."] = ("Get updates from official releases. No automatic download or execution.", "从官方版本页面获取更新，不会自动下载或执行。"),
        ["저장 전에 내용을 확인하세요. 경로·대화·키·훅 설정을 수집하거나 업로드하지 않습니다."] = ("Review before saving. No paths, chats, keys or hook settings are collected or uploaded.", "保存前请检查内容，不收集或上传路径、对话、密钥或钩子设置。"),
        ["JSON 저장"] = ("Save JSON", "保存 JSON"),
        ["저장했어요."] = ("Saved.", "已保存。"),
        ["저장하지 못했어요."] = ("Could not save.", "无法保存。"),
        ["진단 정보를 읽지 못했어요."] = ("Could not read diagnostics.", "无法读取诊断信息。"),
        ["브라우저를 열지 못했어요."] = ("Could not open the browser.", "无法打开浏览器。"),
        ["앱 종료"] = ("Exit", "退出"),
        ["DesktopPet 설정"] = ("DesktopPet Settings", "DesktopPet 设置"),
        ["캐릭터"] = ("Character", "角色"),
        ["갤러리 · 검색"] = ("Gallery · Search", "画廊 · 搜索"),
        ["동작 미리보기"] = ("Animation preview", "动作预览"),
        ["캐릭터 추가"] = ("Add folder", "添加文件夹"),
        ["ZIP 추가"] = ("Add ZIP", "添加 ZIP"),
        ["보관 폴더 열기"] = ("Open library", "打开角色库"),
        ["character.json이 있는 폴더나 캐릭터 ZIP을 추가하세요. 여러 캐릭터가 있으면 하나를 선택할 수 있어요."] = ("Add a character folder or ZIP. Collections let you choose one character.", "添加角色文件夹或 ZIP；合集可选择其中一个角色。"),
        ["크기와 속도"] = ("Size and speed", "大小与速度"),
        ["크기"] = ("Size", "大小"),
        ["걷기 속도"] = ("Speed", "行走速度"),
        ["집중 타이머"] = ("Focus timer", "专注计时器"),
        ["완료한 집중 {0}회 · 이번 실행"] = ("Focus sessions completed: {0} · This run", "本次运行已完成专注：{0} 次"),
        ["횟수 초기화"] = ("Reset count", "重置次数"),
        ["앱을 닫으면 사라지는 횟수예요. 초기화해도 타이머와 알림은 유지해요."] = ("This count clears when the app closes. Resetting keeps the timer and notifications.", "次数在关闭应用后清零。重置不会改变计时器或通知。"),
        ["5분 추가"] = ("Add 5 min", "增加 5 分钟"),
        ["이번 세션에만 5분을 추가해요. 기본 시간은 바뀌지 않아요."] = ("Add 5 minutes to this session only. Default durations stay unchanged.", "仅为当前阶段增加 5 分钟，不更改默认时长。"),
        ["시간 변경은 다음 시작·다시 시작부터 적용해요. 다음 세션은 직접 시작해요."] = ("Duration changes apply on the next start or restart. Start each session manually.", "新时长在下次开始或重启时生效。请手动开始每个阶段。"),
        ["집중(분)"] = ("Focus (min)", "专注（分钟）"),
        ["휴식(분)"] = ("Break (min)", "休息（分钟）"),
        ["시간 적용"] = ("Apply time", "应用时长"),
        ["집중 시작"] = ("Start focus", "开始专注"),
        ["휴식 시작"] = ("Start break", "开始休息"),
        ["일시정지"] = ("Pause", "暂停"),
        ["다음 프레임"] = ("Next frame", "下一帧"),
        ["장면 PNG 저장"] = ("Save scene PNG", "保存场景 PNG"),
        ["기준선"] = ("Frame guides", "辅助线"),
        ["점선은 프레임, 십자는 선언된 기준점입니다."] = ("Dashed outline: frame. Cross: declared anchor in frame coordinates.", "虚线表示帧边界，十字表示帧坐标中的声明锚点。"),
        ["현재 미리보기 장면만 저장해요."] = ("Save only the current preview scene.", "仅保存当前预览场景。"),
        ["저장 중…"] = ("Saving…", "正在保存…"),
        ["PNG 저장됨"] = ("PNG saved", "PNG 已保存"),
        ["PNG 다시 저장"] = ("Retry PNG", "重试保存 PNG"),
        ["장면을 저장하는 중이에요."] = ("Saving scene…", "正在保存场景…"),
        ["장면 PNG를 저장했어요."] = ("Scene PNG saved.", "场景 PNG 已保存。"),
        ["장면을 저장하지 못했어요. 새 PNG 파일 이름을 선택해주세요."] = ("Could not save scene. Choose a new PNG filename.", "无法保存场景，请使用新的 PNG 文件名。"),
        ["{0} · 일시정지"] = ("{0} · paused", "{0} · 已暂停"),
        ["{0} · 착지 후 {1}"] = ("{0} · {1} after landing", "{0} · 落地后{1}"),
        ["계속"] = ("Resume", "继续"),
        ["다시 시작"] = ("Restart", "重新开始"),
        ["종료"] = ("Stop", "停止"),
        ["닫기"] = ("Close", "关闭"),
        ["취소"] = ("Cancel", "取消"),
        ["모션과 화면 갱신"] = ("Motion and rendering", "动效与刷新"),
        ["30 FPS · 절약"] = ("30 FPS · Save work", "30 FPS · 节约"),
        ["60 FPS · 기본"] = ("60 FPS · Default", "60 FPS · 默认"),
        ["120 FPS · 고주사율"] = ("120 FPS · High refresh", "120 FPS · 高刷新率"),
        ["움직임 줄이기 · 점프·강한 알림·창 연출·페이드 끄기"] = ("Reduce motion: pause hops, strong alerts, window effects and fades", "减少动效：暂停跳跃、强提醒、窗口特效与淡入淡出"),
        ["걷기와 발의 접지는 유지합니다. FPS는 최대 갱신 빈도이며 실제 성능은 화면과 PC에 따라 달라요."] = ("Walking and foot contact stay continuous. FPS is a work cap; actual performance depends on your display and PC.", "保留连续行走与脚部接地。FPS 是工作上限，实际性能取决于屏幕和电脑。"),
        ["동작"] = ("Behavior", "行为"),
        ["펫 보이기"] = ("Show pet", "显示宠物"),
        ["자동 산책 잠시 멈춤"] = ("Pause roaming", "暂停自动散步"),
        ["전체화면 앱을 사용할 때 잠시 숨기기"] = ("Pause while a fullscreen app covers this monitor", "当前屏幕全屏时暂时隐藏"),
        ["창 사이를 뛰어다니기"] = ("Hop between windows", "在窗口间跳跃"),
        ["오래 방치하면 잠자기"] = ("Sleep after inactivity", "空闲后睡觉"),
        ["차분한 움직임"] = ("Calm behavior", "安静行为"),
        ["완료 말풍선을 잠시 후 닫기"] = ("Auto-hide completion bubbles", "自动隐藏完成气泡"),
        ["방해 금지 · 말풍선 없이 보관"] = ("Do not disturb · Save without bubbles", "免打扰 · 仅保存通知"),
        ["매일 예약 방해 금지"] = ("Daily quiet hours", "每日预约免打扰"),
        ["예약 적용"] = ("Apply schedule", "应用时段"),
        ["로컬 시간 HH:mm. 자정을 넘길 수 있고 같은 시간은 예약하지 않아요."] = ("Local HH:mm. Overnight intervals work; equal times are inactive.", "本地 HH:mm，支持跨午夜；起止相同则不启用时段。"),
        ["테스트 알림"] = ("Test notification", "测试通知"),
        ["복사"] = ("Copy", "复制"),
        ["복사했어요"] = ("Copied", "已复制"),
        ["다시 복사"] = ("Copy again", "重试复制"),
        ["알림 내용 복사"] = ("Copy notification text", "复制通知内容"),
        ["창 닫을 때 떨어지는 연출"] = ("Window closing fall effect", "窗口关闭下落特效"),
        ["창 연출을 끄면 다른 프로그램의 창을 주기적으로 캡처하지 않아 더 가볍습니다."] = ("Disabling window effects also disables periodic capture of other app windows.", "关闭窗口特效也会关闭其他应用窗口的定期捕获。"),
        ["설정 백업과 복원"] = ("Settings backup and restore", "设置备份与恢复"),
        ["설정 백업"] = ("Back up settings", "备份设置"),
        ["설정 복원"] = ("Restore settings", "恢复设置"),
        ["화면·동작·시간 설정만 옮깁니다. AI 연결·훅 허용·개인 경로는 포함하지 않아요."] = ("Transfer display, behavior and timers only. AI connections, hook trust and private paths stay here.", "仅转移显示、行为与计时设置，不包含 AI 连接、钩子信任或私人路径。"),
        ["움직임 시험"] = ("Motion preview", "动作测试"),
        ["DesktopPet 움직임 시험 · {0}"] = ("DesktopPet Motion Preview · {0}", "DesktopPet 动作预览 · {0}"),
        ["펫을 잡아 옮기거나 던져보세요. 이 창 안에서만 움직입니다."] = ("Drag or toss the pet. It stays inside this preview.", "拖动或抛起宠物，它只会在此预览窗口中移动。"),
        ["처음으로"] = ("Reset", "重置"),
        ["점프"] = ("Jump", "跳跃"),
        ["위에서 놓기"] = ("Drop from above", "从上方放下"),
        ["발을 모으는 중"] = ("Settling its feet", "正在站稳"),
        ["걷는 중 · 앞을 살펴요"] = ("Walking · looking ahead", "行走中 · 看前方"),
        ["걷는 중 · 고개를 돌려요"] = ("Walking · glancing around", "行走中 · 转头看看"),
        ["걷는 중 · 진행 방향을 봐요"] = ("Walking · facing the direction of travel", "行走中 · 看向行走方向"),
        ["공중"] = ("Airborne", "在空中"),
        ["잡고 있는 중"] = ("Being held", "正被拖动"),
        ["바라보는 중"] = ("Watching the pointer", "看着鼠标"),
        ["쉬는 중"] = ("Resting", "休息中"),
        ["AI 연결"] = ("AI connections", "AI 连接"),
        ["설정..."] = ("Settings...", "设置..."),
        ["AI 연결..."] = ("AI connections...", "AI 连接..."),
        ["보관된 알림 확인"] = ("Review saved notifications", "查看已保存通知"),
        ["알림 모두 확인"] = ("Mark all notifications read", "全部标为已读"),
        ["알림 확인 처리 중..."] = ("Marking notifications read...", "正在标记通知..."),
        ["{0}개 알림을 확인하지 못했어요. 보관된 알림에서 다시 시도해주세요."] =
            ("Could not mark {0} notifications read. Retry from saved notifications.", "有 {0} 条通知未能标记，请从已保存通知中重试。"),
        ["보관된 알림 없음"] = ("No saved notifications", "没有已保存通知"),
        ["기분 표현"] = ("Emotes", "表情"),
        ["울기 😢"] = ("Cry 😢", "哭泣 😢"),
        ["화내기 💢"] = ("Angry 💢", "生气 💢"),
        ["집중"] = ("Focus", "专注"),
        ["휴식"] = ("Break", "休息"),
        ["집중 대기"] = ("Ready to focus", "等待专注"),
        [" 일시정지"] = (" paused", "已暂停"),
        [" 완료"] = (" complete", "已完成"),
        ["가만히 있기"] = ("Idle", "静止"),
        ["걷기"] = ("Walk", "行走"),
        ["울기"] = ("Cry", "哭泣"),
        ["화내기"] = ("Angry", "生气"),
        ["잠자기"] = ("Sleep", "睡觉"),
        ["떨어지기"] = ("Fall", "下落"),
        ["걷는 방향을 바라보며 움직여요."] = ("Faces the direction of travel.", "朝行走方向移动。"),
        ["동작을 골라 미리 볼 수 있어요."] = ("Choose an animation to preview.", "选择动作进行预览。"),
        ["이미지를 읽을 수 없습니다."] = ("Cannot read the artwork.", "无法读取图像。"),
        ["캐릭터를 추가하고 있어요…"] = ("Installing character…", "正在安装角色…"),
        ["캐릭터 추가를 취소했어요."] = ("Character installation cancelled.", "已取消角色安装。"),
        ["새 캐릭터를 추가했어요."] = ("Character installed.", "已安装角色。"),
        ["추가하지 못했어요. 캐릭터 팩과 사용 권한을 확인해주세요."] = ("Installation failed. Check the pack and file permissions.", "安装失败，请检查角色包与文件权限。"),
        ["보관 폴더를 열지 못했어요. 폴더의 사용 권한을 확인해주세요."] = ("Cannot open the library. Check folder permissions.", "无法打开角色库，请检查文件夹权限。"),
        ["추가할 캐릭터 폴더 선택"] = ("Choose a character folder", "选择角色文件夹"),
        ["추가할 캐릭터 ZIP 선택"] = ("Choose a character ZIP", "选择角色 ZIP"),
        ["ZIP 캐릭터 선택"] = ("Choose a ZIP character", "选择 ZIP 角色"),
        ["추가할 캐릭터를 선택하세요."] = ("Choose a character to install.", "选择要安装的角色。"),
        ["선택한 캐릭터 추가"] = ("Install selected character", "安装所选角色"),
        ["집중 1~180분, 휴식 1~60분을 입력해주세요."] = ("Enter focus 1–180 min and break 1–60 min.", "请输入专注 1–180 分钟、休息 1–60 分钟。"),
        ["다음 시작·다시 시작부터 적용해요. 진행 중인 시간은 유지합니다."] = ("Applies on the next start/restart. Active time is preserved.", "下次开始或重启时生效，保留当前剩余时间。"),
        ["00:00~23:59 사이 HH:mm 형식으로 입력해주세요."] = ("Enter HH:mm between 00:00 and 23:59.", "请输入 00:00 至 23:59 的 HH:mm 时间。"),
        ["같은 시작·종료 시간은 예약을 활성화하지 않아요."] = ("Equal start/end times leave the schedule inactive.", "起止相同则不启用时段。"),
        ["예약을 적용했어요. 미확인 알림은 계속 보관합니다."] = ("Schedule applied. Unread notifications stay saved.", "时段已应用，仍会保存未读通知。"),
        ["설정을 백업했어요. AI 연결 정보는 포함하지 않았습니다."] = ("Settings backed up. AI connection data is excluded.", "已备份设置，不包含 AI 连接信息。"),
        ["백업하지 못했어요. 저장 위치와 권한을 확인해주세요."] = ("Backup failed. Check the destination and permissions.", "备份失败，请检查位置与权限。"),
        ["설정을 복원했어요. 설치되지 않은 캐릭터 대신 현재 친구를 유지합니다."] = ("Settings restored. The missing character was replaced by your current pet.", "设置已恢复；未安装的角色保留为当前宠物。"),
        ["설정을 복원했어요. AI 연결과 진행 중인 집중 시간은 유지합니다."] = ("Settings restored. AI connections and the active countdown are preserved.", "设置已恢复，保留 AI 连接与当前倒计时。"),
        ["복원하지 못했어요. 백업 형식과 캐릭터 파일을 확인해주세요."] = ("Restore failed. Check the backup format and character files.", "恢复失败，请检查备份格式与角色文件。"),
        ["집중 시간 완료"] = ("Focus complete", "专注完成"),
        ["휴식 시간 완료"] = ("Break complete", "休息完成"),
        ["잠깐 쉬어가세요. 준비되면 휴식을 시작해요."] = ("Take a moment. Start a break when ready.", "休息一下，准备好后开始休息阶段。"),
        ["쉬는 시간이 끝났어요. 준비되면 다시 집중해요."] = ("Your break ended. Start focusing when ready.", "休息结束，准备好后再次开始专注。"),
        ["알림이 잘 도착했어요. 확인해보세요."] = ("The notification arrived. Try acknowledging it.", "通知已到达，请尝试确认。"),
        ["한국어"] = ("한국어", "한국어"),
        ["언어"] = ("Language", "语言"),
        ["Windows 언어 사용"] = ("Use Windows language", "使用 Windows 语言"),
        ["보관된 알림 확인 ({0})"] = ("Review saved notifications ({0})", "查看已保存通知（{0}）"),
        ["DesktopPet · 알림 {0}개"] = ("DesktopPet · {0} unread", "DesktopPet · {0} 条未读"),
        ["추가한 캐릭터"] = ("Custom", "自定义"),
        ["오리지널"] = ("Original", "原创"),
        ["팬아트 팩"] = ("Fan-art pack", "同人包"),
        ["전체 친구"] = ("All characters", "全部角色"),
        ["친구를 선택해보세요."] = ("Choose a companion.", "选择一个伙伴。"),
        ["미리보기는 현재 친구를 바꾸지 않아요."] = ("Preview keeps the current pet unchanged.", "预览不会更换当前宠物。"),
    };
}
