using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SempaPumpStation.Infrastructure;
using TerakkiFramework.Plc;
using TerakkiFramework.Plc.Abstractions;
using TerakkiFramework.Mvvm.Commands;

namespace SempaPumpStation.Screens
{
    /// <summary>Definition of one recipe field: label + which tag it edits.</summary>
    public sealed class RecipeFieldDef
    {
        public string Label;
        public string TagName;
        public string Format;
        public RecipeFieldDef(string label, string tagName, string format = "0.##")
        { Label = label; TagName = tagName; Format = format; }
    }

    /// <summary>A live, bindable recipe field (carries the actual tag for the editor to bind).</summary>
    public sealed class RecipeFieldVm
    {
        public string Label { get; set; }
        public IPlcTag Tag { get; set; }
        public string Format { get; set; }
    }

    /// <summary>
    /// Generic recipe-editor view-model — a modern replacement for the per-family Winlog recipe pages
    /// (Firma/Pompa/Motor/Hat/Testi Yapan). Lists saved recipes, loads them into the edit-buffer tags,
    /// saves the current values to a file, deletes, and "selects" a recipe into the report (WRT_*)
    /// tags. Subclasses only declare the category, fields and the field→report mapping.
    /// </summary>
    public abstract class RecipeEditorViewModel : ScreenBase
    {
        protected RecipeEditorViewModel(HmiContext ctx) : base(ctx)
        {
            foreach (var d in Defs)
                Fields.Add(new RecipeFieldVm { Label = d.Label, Tag = ctx.Tag(d.TagName), Format = d.Format });

            SaveCommand = new SyncCommand(Save);
            DeleteCommand = new SyncCommand(Delete, () => !string.IsNullOrEmpty(SelectedRecipe));
            LoadCommand = new SyncCommand(() => LoadInto(SelectedRecipe), () => !string.IsNullOrEmpty(SelectedRecipe));
            NewCommand = new SyncCommand(NewRecipe);
            SelectForReportCommand = new SyncCommand(SelectForReport);

            RefreshList();
        }

        public abstract string Category { get; }
        protected abstract IReadOnlyList<RecipeFieldDef> Defs { get; }
        /// <summary>Map of edit-buffer tag → report (WRT_*) tag, applied on "select".</summary>
        protected virtual IReadOnlyDictionary<string, string> ReportMap => _empty;
        private static readonly Dictionary<string, string> _empty = new Dictionary<string, string>();

        public ObservableCollection<RecipeFieldVm> Fields { get; } = new ObservableCollection<RecipeFieldVm>();
        public ObservableCollection<string> Recipes { get; } = new ObservableCollection<string>();

        private string _selected;
        public string SelectedRecipe
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;
                if (!string.IsNullOrEmpty(value)) NewName = value;
                DeleteCommand.RaiseCanExecuteChanged();
                LoadCommand.RaiseCanExecuteChanged();
            }
        }

        private string _newName = "";
        public string NewName { get => _newName; set => Set(ref _newName, value); }

        public ICommand SaveCommand { get; }
        public SyncCommand DeleteCommand { get; }
        public SyncCommand LoadCommand { get; }
        public ICommand NewCommand { get; }
        public ICommand SelectForReportCommand { get; }

        private void RefreshList()
        {
            Recipes.Clear();
            foreach (var r in RecipeStore.List(Category)) Recipes.Add(r);
        }

        private void Save()
        {
            var name = string.IsNullOrWhiteSpace(NewName) ? SelectedRecipe : NewName;
            if (string.IsNullOrWhiteSpace(name)) return;

            var data = new Dictionary<string, string>();
            foreach (var d in Defs)
            {
                var v = Ctx.Tag(d.TagName)?.TagValue;
                data[d.TagName] = ToText(v);
            }
            RecipeStore.Save(Category, name, data);
            RefreshList();
            SelectedRecipe = name;
        }

        private void Delete()
        {
            if (string.IsNullOrEmpty(SelectedRecipe)) return;
            RecipeStore.Delete(Category, SelectedRecipe);
            RefreshList();
        }

        private void LoadInto(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            var data = RecipeStore.Load(Category, name);
            foreach (var d in Defs)
            {
                if (!data.TryGetValue(d.TagName, out var text)) continue;
                Apply(d.TagName, text);
            }
        }

        private void NewRecipe()
        {
            foreach (var d in Defs)
            {
                var t = Ctx.Tag(d.TagName);
                if (t == null) continue;
                Ctx.SetFromDevice(d.TagName, IsNumeric(t.DataType) ? (object)0.0 : "");
            }
            SelectedRecipe = null;
            NewName = "";
        }

        private void SelectForReport()
        {
            foreach (var kv in ReportMap)
            {
                var v = Ctx.Tag(kv.Key)?.TagValue;
                Ctx.SetFromDevice(kv.Value, v);
            }
            Ctx.Navigate("Report");
        }

        private void Apply(string tagName, string text)
        {
            var t = Ctx.Tag(tagName);
            if (t == null) return;
            if (IsNumeric(t.DataType))
            {
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                    Ctx.SetFromDevice(tagName, d);
            }
            else Ctx.SetFromDevice(tagName, text);
        }

        private static string ToText(object v)
        {
            if (v == null) return "";
            if (v is string s) return s;
            try { return System.Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture); }
            catch { return v.ToString(); }
        }

        private static bool IsNumeric(DataType t)
        {
            switch (t)
            {
                case DataType.STRING: case DataType.WSTRING:
                case DataType.CHAR: case DataType.WCHAR:
                case DataType.BOOL: case DataType.MOMENTARY_BOOL:
                    return false;
                default: return true;
            }
        }
    }
}
