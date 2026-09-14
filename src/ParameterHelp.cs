using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
internal sealed partial class PadHop {
 void Explain(TextBlock label,string text){string help=ParameterHelp(text);if(help==null)return;help=help.Replace("。", "。\n").Trim();label.Inlines.Add(new Run("  ?"){Foreground=new SolidColorBrush(Color.FromRgb(97,205,250)),FontWeight=FontWeights.Bold});label.ToolTip=new ToolTip{Background=new SolidColorBrush(Color.FromRgb(28,43,56)),Foreground=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(64,93,114)),Padding=new Thickness(12),Content=new TextBlock{Text=help,MaxWidth=300,TextWrapping=TextWrapping.Wrap,LineHeight=22,Foreground=Brushes.White}};ToolTipService.SetInitialShowDelay(label,180);ToolTipService.SetShowDuration(label,30000);System.Windows.Automation.AutomationProperties.SetHelpText(label,help);}
 string ParameterHelp(string label){
 if(label.StartsWith(L.T("触摸缓冲")))return L.T("刚接触触摸板后，暂缓滑动反馈多久。调大更能避开按压准备动作，调小滑动反馈来得更快。只影响 Steamless 触觉规则，不延迟鼠标移动。");
 if(label.StartsWith(L.T("松开缓冲")))return L.T("点击松开后，暂停滑动反馈多久。调大可减少松开后的杂震，调小更快恢复滑动反馈。不会延迟松开反馈或鼠标移动。");
 if(label.StartsWith(L.T("按压避让阈值")))return L.T("压力高于此值时不触发滑动震动，避免按压时混入滑动点振。调低更保守，调高允许较重的手指继续得到滑动反馈。不是鼠标点击的按下力度。");
 if(label.StartsWith(L.T("通电时长")))return L.T("每个脉冲通电多久，单位微秒（1000 μs = 1 ms）。调大通常更明显、更厚重；调小通常更短、更轻。它不是音调频率。建议从录制值附近小幅调整。");
 if(label.StartsWith(L.T("关闭间隔")))return L.T("同一次反馈中，相邻脉冲之间暂停多久。调大会让脉冲更分离；0 表示紧接着发送。重复次数为 1 时，没有相邻脉冲，这个值不用于调节两次滑动反馈的间隔。");
 if(label.StartsWith(L.T("重复次数")))return L.T("一次触发包含几个脉冲。1 是单次短促反馈；调大通常更持续、更像一串震动。通电与关闭时长乘以次数必须在 80 ms 限制内。");
 if(label.StartsWith(L.T("滑动最短间隔")))return L.T("两次滑动反馈至少相隔多久。调小可能更密，调大更稀疏。还必须达到触发距离，因此不是固定每隔这些毫秒就震一下；只影响滑动反馈。");
 if(label.StartsWith(L.T("滑动触发距离")))return L.T("手指持续滑动累计多远才允许下一次反馈。调小更容易、更频繁触发；调大需要滑得更远。这里的 px 是按固定比例换算的移动量，不随鼠标速度设置改变，也不等同于实际屏幕像素。还受最短间隔限制。");
 if(label.StartsWith(L.T("频率 Hz")))return L.T("短音调每秒振动的次数。改变震动质感，不代表强弱：更高的 Hz 不一定更强，也可能更难感觉到。需要结合时长和强度体验。");
 if(label.StartsWith(L.T("时长 ms")))return L.T("一次短音调持续多久。调大更持久，调小更利落；最长 80 ms。与滑动反馈之间的间隔是不同参数。");
 if(label.StartsWith(L.T("强度 dB")))return L.T("内置反馈或音调的增益。越接近 0 越强，例如 -3 dB 比 -12 dB 强；越负越弱。当前允许 -30 到 0 dB。实际触感也取决于反馈类型。");
 if(label.StartsWith(L.T("类型（")))return L.T("1 为内置点振，2 为内置点击，是手柄固件提供的两种效果。数字表示类型，不表示强度；强弱由旁边的 dB 调整。");
 if(label.StartsWith(L.T("移动速度")))return L.T("手指移动相同距离时，鼠标移动多少。调大移动更快、更远，调小更精细。仅对“移动鼠标”动作生效，不改变滚轮速度。");
 if(label.StartsWith(L.T("平滑 ms")))return L.T("鼠标移动的平滑程度。调大可以减少抖动，但跟手性可能降低；调小反应更直接。0 为关闭平滑。");
 if(label.StartsWith(L.T("滚轮距离")))return L.T("每滚动一格需要累计的触摸板位移。越小滚动越快，越大滚动越慢。只影响“滚动内容”动作，不影响鼠标速度。");
 if(label.StartsWith(L.T("按下力度")))return L.T("压力达到这个阈值后视为按下。调低更容易触发，也更容易误触；调高需要更用力。此数值是传感器读数，不是克数。");
 if(label.StartsWith(L.T("松开力度")))return L.T("按下后，压力低于此值才视为松开。必须低于按下力度；差距较大可减少边界抖动，但需要松得更彻底。");
 if(label.StartsWith(L.T("惯性摩擦")))return L.T("轨迹球模式中，手指离开后鼠标滑行的减速程度。调大停得更快，调小滑得更远。仅在启用轨迹球惯性时影响鼠标移动。");
 if(label==L.T("编辑哪个事件"))return L.T("滑动反馈：手指持续移动时触发。按压反馈：触摸板按下时触发。Steamless 规则使用经过确认的硬件点击信号，原有规则跟随鼠标压力阈值。三种反馈独立保存，松开反馈：触摸板松开时触发，可独立调整或关闭。");
 if(label==L.T("反馈方式"))return L.T("短脉冲\n调整一次短促反馈的通电时长、间隔和次数。\n内置反馈\n选择固件效果，并调整强度。\n短音调\n调整频率、持续时间和强度。\n关闭\n此事件不输出震动。");
 if(label==L.T("滑动动作"))return L.T("选择当前触摸板控制鼠标、滚动内容，或不映射滑动。左右板可独立设置；触觉反馈另行配置。");
 if(label==L.T("按压动作"))return L.T("选择按下当前触摸板时发送鼠标左键、右键或不映射。阈值在移动与按压参数中设置；不占用手柄扳机。");
 if(label.StartsWith(L.T("录制来源")))return L.T("选择 Steam 当前提供触觉反馈的那块板作为来源。录制结果应用到当前编辑的目标板，来源与目标可以不同。录制前保留来源板的 Steam 绑定和触觉。");
 return null;
 }
}
