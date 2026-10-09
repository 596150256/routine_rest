using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace RoutineRest.App;

public partial class WaterWindow : Window
{
    private readonly App app;
    private bool submitted;
    public WaterWindow(App app) { this.app = app; InitializeComponent(); }
    private void PresetClick(object sender, RoutedEventArgs e) { if (sender is Button button && button.Tag is string amount) Amount.Text = amount; }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(Amount.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int amount) || amount < 0 || amount > 5000)
        { ErrorLabel.Text = "请输入 0–5000 之间的整数毫升数。"; return; }
        Save(amount);
    }
    private void NoneClick(object sender, RoutedEventArgs e) => Save(0);
    private void Save(int amount)
    {
        if (submitted) return;
        if (!app.TryRecordWater(amount, out string error))
        {
            ErrorLabel.Text = "保存失败，记录未提交；请检查数据目录后重试。";
            ErrorLabel.ToolTip = error;
            return;
        }
        submitted = true;
        Close();
    }
}
