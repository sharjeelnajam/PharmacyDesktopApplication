namespace PharmacyDesktop.UI.Pages;

/// <summary>Base for every screen hosted in the main window. Pages are cached; OnPageShown refreshes data.</summary>
public class PageBase : UserControl
{
    public PageBase()
    {
        BackColor = Theme.Background;
        Font = Theme.Body;
        DoubleBuffered = true;
    }

    public virtual string Title => "";
    public virtual string Subtitle => "";

    /// <summary>Called every time the page becomes visible.</summary>
    public virtual void OnPageShown() { }

    /// <summary>Page-specific keyboard shortcuts. Return true when handled.</summary>
    public virtual bool HandleKey(Keys key) => false;
}
