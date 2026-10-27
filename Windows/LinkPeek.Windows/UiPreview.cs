namespace LinkPeek.Windows;

// Uses only synthetic data and DrawToBitmap; never captures the user's screen,
// reads credentials, sends API requests, or changes settings/startup registration.
internal static class UiPreview
{
    public static void Render(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var dark in new[] { false, true })
        {
            Palette.PreviewDark = dark;
            var theme = dark ? "dark" : "light";
            foreach (var page in new[] { "general", "azure", "about" })
            {
                using var settings = new SettingsForm(new Preferences { Organization = "contoso" }, () => { }, preview: true);
                settings.PreviewPage(page); Save(settings, $"settings-{page}-{theme}.png");
            }
            var web = WebLink.Parse("https://github.com/nqnhut92/LinkPeek")!;
            using var qr = new CardForm(web, null, null, new Preferences(), true, () => { }, () => { });
            Save(qr, $"qr-{theme}.png");
            var prLink = WebLink.Parse("https://dev.azure.com/contoso/Platform/_git/linkpeek/pullrequest/142")!;
            var snapshot = new Snapshot(new PullRequest(142, "Make links feel at home on Windows", "active", false,
                new Commit(new string('a', 40)), new Repository("sample", "linkpeek"), new Person("sample", "Alex Chen"),
                "refs/heads/feature/windows", "refs/heads/main"), "preview", 0);
            using var pr = new CardForm(prLink, null, null, new Preferences(), true, () => { }, () => { }, snapshot);
            Save(pr, $"pull-request-{theme}.png");
        }
        Palette.PreviewDark = null;

        void Save(Form form, string name)
        {
            form.Show(); Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
            if (form.Width < 100 || form.Height < 100) throw new InvalidOperationException("Invalid UI preview dimensions.");
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(directory, name), System.Drawing.Imaging.ImageFormat.Png); form.Close();
        }
    }
}
