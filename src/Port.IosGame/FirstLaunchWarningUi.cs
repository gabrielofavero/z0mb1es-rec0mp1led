using Foundation;
using Port.Prepare;
using UIKit;

namespace Port.IosGame;

/// <summary>Static app UI shown before constructing the original game or importing assets.</summary>
internal static class FirstLaunchWarningUi
{
    private static UIWindow? window;

    internal static void ShowIfNeeded(Action ready, Action<Exception> failed)
    {
        if (FirstLaunchWarning.IsAcknowledged(GameData.Saves)) { ready(); return; }
        var scene = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
            .OrderBy(s => s.ActivationState == UISceneActivationState.ForegroundActive ? 0 : 1).FirstOrDefault()
            ?? throw new InvalidOperationException("UIKit did not provide a window scene for the flashing lights warning.");
        var bounds = OperatingSystem.IsIOSVersionAtLeast(26)
            ? scene.EffectiveGeometry.CoordinateSpace.Bounds : scene.CoordinateSpace.Bounds;
        window = new UIWindow(scene)
        {
            Frame = bounds,
            WindowLevel = UIWindowLevel.Normal + 3,
            RootViewController = new FlashingWarningController(() =>
            {
                window!.Hidden = true;
                CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
                {
                    try { ready(); }
                    catch (Exception error) { failed(error); }
                });
            })
        };
        window.MakeKeyAndVisible();
        GameTrace.Write("flashing-warning=presented");
    }
}

internal sealed class FlashingWarningController(Action ready) : UIViewController
{
    private bool reduceFlashing = FirstLaunchWarning.InitialReduceFlashing(GameData.Saves);

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        var title = new UILabel
        {
            Text = FirstLaunchWarning.Title, Lines = 0,
            Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Title2),
            AdjustsFontForContentSizeCategory = true, TranslatesAutoresizingMaskIntoConstraints = false
        };
        var text = new UITextView
        {
            Text = FirstLaunchWarning.Text, Editable = false, Selectable = false, ScrollEnabled = true,
            BackgroundColor = UIColor.Clear, TextColor = UIColor.Label,
            Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body),
            AdjustsFontForContentSizeCategory = true, TranslatesAutoresizingMaskIntoConstraints = false
        };
        var checkbox = new UIButton(UIButtonType.System) { TranslatesAutoresizingMaskIntoConstraints = false };
        checkbox.SetTitle("  " + FirstLaunchWarning.CheckboxLabel, UIControlState.Normal);
        checkbox.TitleLabel!.Lines = 0;
        checkbox.TitleLabel.Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body);
        checkbox.TitleLabel.AdjustsFontForContentSizeCategory = true;
        checkbox.AccessibilityLabel = FirstLaunchWarning.CheckboxLabel;
        void UpdateCheckbox()
        {
            checkbox.SetImage(UIImage.GetSystemImage(reduceFlashing ? "checkmark.square" : "square"), UIControlState.Normal);
            checkbox.AccessibilityValue = reduceFlashing ? "Checked" : "Unchecked";
            checkbox.AccessibilityTraits = UIAccessibilityTrait.Button |
                (reduceFlashing ? UIAccessibilityTrait.Selected : UIAccessibilityTrait.None);
        }
        checkbox.TouchUpInside += (_, _) => { reduceFlashing = !reduceFlashing; UpdateCheckbox(); };
        UpdateCheckbox();
        var status = new UILabel { Lines = 0, TextColor = UIColor.SystemRed,
            TranslatesAutoresizingMaskIntoConstraints = false };
        var proceed = new UIButton(UIButtonType.System) { TranslatesAutoresizingMaskIntoConstraints = false };
        proceed.SetTitle("Continue", UIControlState.Normal);
        proceed.TitleLabel!.Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Headline);
        proceed.TitleLabel.AdjustsFontForContentSizeCategory = true;
        proceed.TouchUpInside += (_, _) =>
        {
            proceed.Enabled = false;
            try
            {
                FirstLaunchWarning.Acknowledge(GameData.Saves, reduceFlashing);
                GameTrace.Write("flashing-warning=acknowledged reduceFlashing=" + reduceFlashing);
            }
            catch (Exception error)
            {
                status.Text = "Could not save your choice. " + error.GetBaseException().Message;
                proceed.Enabled = true;
                return;
            }
            ready();
        };
        var panel = new UIView { TranslatesAutoresizingMaskIntoConstraints = false };
        View.AddSubview(panel);
        foreach (var control in new UIView[] { title, text, checkbox, status, proceed }) panel.AddSubview(control);
        var safe = View.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints([
            panel.TopAnchor.ConstraintEqualTo(safe.TopAnchor, 20),
            panel.BottomAnchor.ConstraintEqualTo(safe.BottomAnchor, -16),
            panel.CenterXAnchor.ConstraintEqualTo(safe.CenterXAnchor),
            panel.LeadingAnchor.ConstraintGreaterThanOrEqualTo(safe.LeadingAnchor, 20),
            panel.TrailingAnchor.ConstraintLessThanOrEqualTo(safe.TrailingAnchor, -20),
            panel.WidthAnchor.ConstraintLessThanOrEqualTo(600),
            title.TopAnchor.ConstraintEqualTo(panel.TopAnchor),
            title.LeadingAnchor.ConstraintEqualTo(panel.LeadingAnchor), title.TrailingAnchor.ConstraintEqualTo(panel.TrailingAnchor),
            text.TopAnchor.ConstraintEqualTo(title.BottomAnchor, 12),
            text.LeadingAnchor.ConstraintEqualTo(panel.LeadingAnchor), text.TrailingAnchor.ConstraintEqualTo(panel.TrailingAnchor),
            text.BottomAnchor.ConstraintEqualTo(checkbox.TopAnchor, -8),
            checkbox.LeadingAnchor.ConstraintEqualTo(panel.LeadingAnchor), checkbox.TrailingAnchor.ConstraintEqualTo(panel.TrailingAnchor),
            checkbox.HeightAnchor.ConstraintGreaterThanOrEqualTo(44),
            status.TopAnchor.ConstraintEqualTo(checkbox.BottomAnchor, 4),
            status.LeadingAnchor.ConstraintEqualTo(panel.LeadingAnchor), status.TrailingAnchor.ConstraintEqualTo(panel.TrailingAnchor),
            proceed.TopAnchor.ConstraintEqualTo(status.BottomAnchor, 8),
            proceed.LeadingAnchor.ConstraintEqualTo(panel.LeadingAnchor), proceed.TrailingAnchor.ConstraintEqualTo(panel.TrailingAnchor),
            proceed.HeightAnchor.ConstraintGreaterThanOrEqualTo(44), proceed.BottomAnchor.ConstraintEqualTo(panel.BottomAnchor)
        ]);
        // Fill the available width on phones and cap the reading width on tablets.
        var width = panel.WidthAnchor.ConstraintEqualTo(safe.WidthAnchor, -40);
        width.Priority = 999;
        width.Active = true;
    }
}
