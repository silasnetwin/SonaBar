namespace SonaBar.App;

public static class QuickActions
{
    public static ContextMenu Create(AppController controller)
    {
        var menu = new ContextMenu();
        void Add(string title, Func<Task> action, bool enabled = true)
        {
            var item = new MenuItem { Header = title, IsEnabled = enabled && !controller.Busy }; item.Click += async (_, _) => { try { await action(); } catch (OperationCanceledException) { } catch (Exception error) { controller.Notify(error.Message); } }; menu.Items.Add(item);
        }
        var hasText = controller.LastText.Length > 0;
        Add("Copy", () => { controller.Clipboard.SetText(controller.LastText); return Task.CompletedTask; }, hasText);
        Add("Undo insertion", controller.UndoAsync, controller.CanUndo);
        Add("Retry text processing", controller.RetryProcessingAsync, controller.LastOriginal.Length > 0);
        menu.Items.Add(new Separator());
        foreach (var id in new[] { "grammar", "professional", "casual", "shorten", "expand", "prompt" })
        {
            var action = RewriteActions.All.First(x => x.Id == id); Add(action.Label, () => controller.RewriteLatestAsync(action), hasText);
        }
        var more = new MenuItem { Header = "More styles…", IsEnabled = hasText && !controller.Busy };
        foreach (var action in RewriteActions.All.Where(x => new[] { "friendly", "serious", "funny", "romantic", "simple", "direct", "polite", "bullets", "tasks" }.Contains(x.Id)))
        {
            var item = new MenuItem { Header = action.Label }; item.Click += async (_, _) => { try { await controller.RewriteLatestAsync(action); } catch (Exception error) { controller.Notify(error.Message); } }; more.Items.Add(item);
        }
        menu.Items.Add(more);
        var translate = new MenuItem { Header = "Translate…", IsEnabled = hasText && !controller.Busy };
        foreach (var language in new[] { "English", "German", "French", "Spanish", "Italian", "Portuguese", "Chinese", "Japanese", "Korean", "Arabic", "Hindi", "Russian", "Turkish", "Polish", "Dutch" })
        {
            var item = new MenuItem { Header = language }; item.Click += async (_, _) => { try { await controller.RewriteLatestAsync(RewriteActions.All.First(x => x.Id == "translate"), language); } catch (Exception error) { controller.Notify(error.Message); } }; translate.Items.Add(item);
        }
        menu.Items.Add(translate); menu.Items.Add(new Separator());
        Add("lowercase", () => controller.SetLatestCasingAsync(OutputCasing.Lowercase), hasText); Add("UPPERCASE", () => controller.SetLatestCasingAsync(OutputCasing.Uppercase), hasText);
        return menu;
    }
}
