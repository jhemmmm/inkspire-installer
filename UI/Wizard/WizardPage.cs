using Inkspire.Panel.Setup;

namespace Inkspire.Panel.UI.Wizard;

public sealed record ValidationResult(bool Ok, string? Message = null)
{
    public static readonly ValidationResult Pass = new(true);
    public static ValidationResult Fail(string message) => new(false, message);
}

/// <summary>What a page may ask of the wizard window.</summary>
public interface IWizardHost
{
    CancellationToken CancelToken { get; }
    void SetNavigation(bool? back = null, bool? next = null, bool? cancel = null, string? nextText = null);
    void GoNext();
    void GoBack();
    /// <summary>Close the wizard. <paramref name="launchPanel"/> starts the installed panel first.</summary>
    void Finish(bool launchPanel);
}

public abstract class WizardPage : UserControl
{
    protected WizardPage()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(24, 12, 24, 12);
    }

    public abstract string Title { get; }
    public virtual string Subtitle => "";
    public virtual string NextText => "Next";
    public virtual bool CanGoBack => true;
    public virtual bool ShowCancel => true;

    public IWizardHost? Host { get; internal set; }

    /// <summary>Fill the controls from the state (called every time the page is shown).</summary>
    public virtual void OnEnter(WizardState state)
    {
    }

    /// <summary>Called before leaving forward. A failure keeps the user on the page and shows the message.</summary>
    public virtual ValidationResult Validate(WizardState state) => ValidationResult.Pass;

    /// <summary>Copy the page's inputs into the state (after a successful <see cref="Validate"/>).</summary>
    public virtual void Save(WizardState state)
    {
    }

    /// <summary>The wizard is closing or going back: stop background work.</summary>
    public virtual void OnLeave()
    {
    }
}
