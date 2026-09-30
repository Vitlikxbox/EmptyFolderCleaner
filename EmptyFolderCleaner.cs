using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

class EmptyFolderCleaner : Form
{
    static readonly string[] JunkFiles = { "desktop.ini", "thumbs.db", ".ds_store" };

    TextBox folderBox;
    CheckBox junkBox;
    Button browseButton, scanButton, deleteButton;
    ListView list;
    Label statusLabel;

    EmptyFolderCleaner(string start)
    {
        Text = "EmptyFolderCleaner";
        ClientSize = new Size(700, 460);
        MinimumSize = new Size(560, 340);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;

        Controls.Add(new Label { Text = "Папка:", Location = new Point(12, 15), AutoSize = true });
        folderBox = new TextBox { Location = new Point(70, 12), Width = 410, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        folderBox.Text = start ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Controls.Add(folderBox);
        browseButton = new Button { Text = "Обзор…", Location = new Point(488, 10), Size = new Size(90, 29), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        browseButton.Click += (s, e) =>
        {
            using (var d = new FolderBrowserDialog { SelectedPath = folderBox.Text })
                if (d.ShowDialog(this) == DialogResult.OK) folderBox.Text = d.SelectedPath;
        };
        Controls.Add(browseButton);
        scanButton = new Button { Text = "Найти", Location = new Point(586, 10), Size = new Size(102, 29), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        scanButton.Click += (s, e) => Scan();
        Controls.Add(scanButton);

        junkBox = new CheckBox { Text = "Считать пустыми папки, где только desktop.ini и Thumbs.db", Checked = true, Location = new Point(12, 48), AutoSize = true };
        Controls.Add(junkBox);

        list = new ListView
        {
            Location = new Point(12, 80), Size = new Size(676, 326), View = View.Details, CheckBoxes = true, FullRowSelect = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        list.Columns.Add("Пустая папка", 520);
        list.Columns.Add("Изменена", 130);
        list.DoubleClick += (s, e) => { if (list.SelectedItems.Count > 0) Process.Start("explorer.exe", "\"" + list.SelectedItems[0].Tag + "\""); };
        list.ItemChecked += (s, e) => UpdateButton();
        Controls.Add(list);

        statusLabel = new Label { Location = new Point(12, 422), AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        statusLabel.Text = "Выбери папку и нажми «Найти». Ничего не удаляется без подтверждения";
        Controls.Add(statusLabel);
        deleteButton = new Button { Text = "Удалить отмеченные", Location = new Point(508, 416), Size = new Size(180, 32), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Enabled = false };
        deleteButton.Click += (s, e) => Delete();
        Controls.Add(deleteButton);

        if (start != null) Shown += (s, e) => Scan();
    }

    void UpdateButton()
    {
        deleteButton.Enabled = list.CheckedItems.Count > 0;
        deleteButton.Text = list.CheckedItems.Count > 0 ? "Удалить (" + list.CheckedItems.Count + ")" : "Удалить отмеченные";
    }

    void Scan()
    {
        string root = folderBox.Text.Trim();
        if (!Directory.Exists(root)) { MessageBox.Show(this, "Папка не найдена", Text); return; }
        root = Path.GetFullPath(root).TrimEnd('\\');
        if (Path.GetPathRoot(root).TrimEnd('\\') == root || root.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Корень диска и системные папки лучше не чистить: пустые папки там часто нужны программам. Выбери конкретную папку, например «Документы» или «Загрузки».", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        bool junk = junkBox.Checked;
        scanButton.Enabled = false;
        list.Items.Clear();
        statusLabel.Text = "Ищу…";
        new Thread(() =>
        {
            var found = new List<string>();
            int checkedDirs = 0;
            foreach (var d in SafeDirs(root)) IsEmpty(d, junk, found, ref checkedDirs);
            BeginInvoke(new Action(() => ShowResults(root, found, checkedDirs)));
        }) { IsBackground = true }.Start();
    }

    static string[] SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); } catch { return new string[0]; }
    }

    // Returns true if the folder has no files (except junk) and all subfolders are empty too.
    // Collects only the topmost empty folders: deleting them removes everything inside.
    static bool IsEmpty(string dir, bool junk, List<string> found, ref int checkedDirs)
    {
        checkedDirs++;
        try
        {
            var info = new DirectoryInfo(dir);
            if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) return false;
            bool hasFiles = info.GetFiles().Any(f => !junk || !JunkFiles.Contains(f.Name.ToLowerInvariant()));
            var subs = info.GetDirectories();
            var emptySubs = new List<string>();
            bool allEmpty = true;
            foreach (var s in subs)
            {
                var inner = new List<string>();
                if (IsEmpty(s.FullName, junk, inner, ref checkedDirs)) emptySubs.Add(s.FullName);
                else { allEmpty = false; found.AddRange(inner); }
            }
            if (!hasFiles && allEmpty) { found.Add(dir); return true; }
            found.AddRange(emptySubs);
            return false;
        }
        catch { return false; }
    }

    void ShowResults(string root, List<string> found, int checkedDirs)
    {
        list.BeginUpdate();
        foreach (var d in found.Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
        {
            string rel = d.Substring(root.Length).TrimStart('\\');
            string date = "";
            try { date = Directory.GetLastWriteTime(d).ToString("dd.MM.yyyy"); } catch { }
            list.Items.Add(new ListViewItem(new[] { rel, date }) { Tag = d, Checked = true });
        }
        list.EndUpdate();
        scanButton.Enabled = true;
        list.Focus();
        statusLabel.Text = "Проверено папок: " + checkedDirs + ", пустых: " + list.Items.Count;
        UpdateButton();
    }

    void Delete()
    {
        var items = list.CheckedItems.Cast<ListViewItem>().ToList();
        if (MessageBox.Show(this, "Переместить в корзину пустых папок: " + items.Count + "?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        int removed = 0;
        foreach (var item in items.OrderByDescending(i => ((string)i.Tag).Length))
        {
            string dir = (string)item.Tag;
            try
            {
                // Re-check right before deleting: something may have appeared since the scan.
                var inner = new List<string>();
                int n = 0;
                if (!IsEmpty(dir, junkBox.Checked, inner, ref n)) continue;
                FileSystem.DeleteDirectory(dir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                list.Items.Remove(item);
                removed++;
            }
            catch { }
        }
        statusLabel.Text = "Удалено в корзину: " + removed;
        UpdateButton();
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.Run(new EmptyFolderCleaner(args.Length > 0 && Directory.Exists(args[0]) ? args[0] : null));
    }
}
