﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Firefall_DISASM_Name_Manager
{
    public partial class MainForm : Form
    {
        string TitleText = "";
        bool EditNameDataMode = true;
        ListViewColumnSorter listViewColumnSorter;
        bool DatabaseLoaded = false;
        string DatabasePath = "";

        public MainForm()
        {
            InitializeComponent();

            MainFormMenuStrip.Renderer = new DarkRenderer();
            NamesListContextMenuStrip.Renderer = new DarkRenderer();

            TitleText = this.Text;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Prevent flickering on resize by forcing the ListView to be double buffered
            NamesListView.GetType().GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(NamesListView, true, null);

            this.ForeColor = Color.DarkGray;
            this.BackColor = Color.FromArgb(255, 30, 30, 30);
            DarkTheme.ApplyTheme_Dark(this);
            DarkTheme.ApplyTheme_ContextMenuStrip_Dark(NamesListContextMenuStrip);

            LoadStatusValues();

            StatusComboBox.SelectedIndex = 0;

            ToggleEditInputMode(true);

            // Clear Designer placeholder values
            NamesListView.Items.Clear();
        }

        private void LoadStatusValues()
        {
            foreach (int status in Enum.GetValues(typeof(ENameStatus)))
            {
                StatusComboBox.Items.Add($"{status}: {Enum.GetName(typeof(ENameStatus), status)}");
            }
        }

        private void SortNamesListView()
        {
            // Temporarily enable sorting, .NET 5 WinForms will crash if adding items to a ListView while the ListViewItemSorter is set.
            listViewColumnSorter = new ListViewColumnSorter();
            NamesListView.ListViewItemSorter = listViewColumnSorter;
            // Sort lower addresses to the top of each category
            listViewColumnSorter.SortColumn = NamesListView.Columns["AddressColumnHeader"].Index;
            listViewColumnSorter.Order = SortOrder.Ascending;
            NamesListView.Sort();
            listViewColumnSorter.SortColumn = NamesListView.Columns["CategoryColumnHeader"].Index;
            listViewColumnSorter.Order = SortOrder.Ascending;
            NamesListView.Sort();
            NamesListView.ListViewItemSorter = null;
        }

        public int AddNamesToListView(List<NameClass> NamesObject, bool UpdateFullDuplicates = true)
        {
            NamesListView.BeginUpdate();

            // Lists of all objects that exist in the ListView already used for duplication checking
            List<NameClass> ListNames = ListViewToNameClass();

            List<NameClass> UpdateObjects = new List<NameClass>();

            Dictionary<string, List<NameClass>> DuplicateObjects = new Dictionary<string, List<NameClass>>()
            {
                { "Address", new List<NameClass>() },
                { "Name", new List<NameClass>() }
            };
            Dictionary<string, List<NameClass>> SourceObjects = new Dictionary<string, List<NameClass>>()
            {
                { "Address", new List<NameClass>() },
                { "Name", new List<NameClass>() }
            };

            List<ListViewItem> SourceDuplicatesToRemove = new List<ListViewItem>();

            // Determine if an object is a duplicate and if so what fields are duplicates
            foreach (NameClass PendingItem in NamesObject)
            {
                int SourceItemIndex = -1;
                foreach (NameClass ExistingItem in ListNames)
                {
                    SourceItemIndex++;
                    bool DupeAddress = PendingItem.Address == ExistingItem.Address;
                    bool DupeName = PendingItem.Name == ExistingItem.Name;

                    if (DupeAddress && DupeName && UpdateFullDuplicates)
                    {
                        if (PendingItem.Address == "" && PendingItem.Name == "")
                        {
                            // This is a category comment and needs to be carefully handled
                            if (PendingItem.Category == ExistingItem.Category)
                            {
                                // Do not consider to be a "duplicate" but rather a metadata update entry
                                UpdateObjects.Add(PendingItem);
                                ListViewItem ViewItem = NamesListView.Items[SourceItemIndex];
                                ViewItem.SubItems["Comment"].Text = PendingItem.Comment;
                                continue;
                            }
                        }
                        else
                        {
                            // Do not consider to be a "duplicate" but rather a metadata update entry
                            UpdateObjects.Add(PendingItem);
                            ListViewItem ViewItem = NamesListView.Items[SourceItemIndex];
                            ViewItem.SubItems["Category"].Text = PendingItem.Category;
                            ViewItem.SubItems["Status"].Text = PendingItem.Status.ToString();
                            ViewItem.SubItems["Comment"].Text = PendingItem.Comment;
                            continue;
                        }
                    }
                    else if (DupeAddress)
                    {
                        DuplicateObjects["Address"].Add(PendingItem);
                        SourceObjects["Address"].Add(ExistingItem);
                        SourceDuplicatesToRemove.Add(NamesListView.Items[SourceItemIndex]);
                        UpdateObjects.Add(PendingItem);
                        continue;
                    }
                    else if (DupeName)
                    {
                        DuplicateObjects["Name"].Add(PendingItem);
                        SourceObjects["Name"].Add(ExistingItem);
                        SourceDuplicatesToRemove.Add(NamesListView.Items[SourceItemIndex]);
                        UpdateObjects.Add(PendingItem);
                        continue;
                    }
                }
            }

            // Remove update-only objects from the list of objects to be added
            UpdateObjects.All(x => NamesObject.Remove(x));

            // Remove duplicate items from the ListView as they will be added back during the deduplication stage if the user selects to keep them
            foreach (ListViewItem item in SourceDuplicatesToRemove)
            {
                NamesListView.Items.Remove(item);
            }

            if (DuplicateObjects["Address"].Count > 0 || DuplicateObjects["Name"].Count > 0)
            {
                DeduplicationForm deduplicationForm = new DeduplicationForm(DuplicateObjects, SourceObjects);
                if (deduplicationForm.ShowDialog(this) == DialogResult.OK)
                {
                    // TODO: Currently this is adding another copy of the "deduplicated" name to the list of what to add
                    // This instead needs to be updating the existing entry.
                    NamesObject.AddRange(deduplicationForm.DeduplicatedItems);
                }
                else
                {
                    NamesListView.EndUpdate();
                    MessageBox.Show("Deduplication process was canceled. The entire import process will now be aborted.\n\nNo data has been imported to the database.", "Deduplication canceled", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    // Return -1 to allow detecting when importing was canceled and not that 0 items were in the import table
                    return -1;
                }
            }

            // Add new item to the ListView
            foreach (NameClass item in NamesObject)
            {
                ListViewItem.ListViewSubItem Category = new ListViewItem.ListViewSubItem
                {
                    Text = item.Category,
                    Name = "Category"
                };
                ListViewItem.ListViewSubItem Address = new ListViewItem.ListViewSubItem
                {
                    Text = item.Address,
                    Name = "Address"
                };
                ListViewItem.ListViewSubItem Name = new ListViewItem.ListViewSubItem
                {
                    Text = item.Name,
                    Name = "Name"
                };
                ListViewItem.ListViewSubItem Status = new ListViewItem.ListViewSubItem
                {
                    Text = item.Status.ToString(),
                    Name = "Status"
                };
                ListViewItem.ListViewSubItem Comment = new ListViewItem.ListViewSubItem
                {
                    Text = item.Comment,
                    Name = "Comment"
                };

                NamesListView.Items.Add(new ListViewItem(new ListViewItem.ListViewSubItem[] { Category, Address, Name, Status, Comment }, -1));
            }

            UpdateCategoryAutoComplete();

            SortNamesListView();

            NamesListView.EndUpdate();

            return NamesObject.Count;
        }

        private List<NameClass> ListViewToNameClass()
        {
            List<NameClass> NamesObject = new List<NameClass>();
            foreach (ListViewItem item in NamesListView.Items)
            {
                NameClass NameObject = new NameClass();
                NameObject.Category = item.SubItems["Category"].Text;
                NameObject.Address = item.SubItems["Address"].Text;
                NameObject.Name = item.SubItems["Name"].Text;
                NameObject.Status = int.Parse(item.SubItems["Status"].Text);
                NameObject.Comment = item.SubItems["Comment"].Text;

                NamesObject.Add(NameObject);
            }

            return NamesObject;
        }

        private void WriteNamesToFile(string FilePath)
        {
            StringBuilder FileContents = new StringBuilder();
            FileContents.AppendLine($"// Version #{Globals.DATABASEVERSION}");
            FileContents.AppendLine("// Firefall DISASM Name Manager Database");
            FileContents.AppendLine($"// FirefallClient.exe V{Globals.TargetClientVersion}");
            FileContents.Append(JsonSerializer.Serialize<List<NameClass>>(ListViewToNameClass(), new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
            File.WriteAllText(FilePath, FileContents.ToString());
        }

        private bool LoadNamesFromDatabase(string FilePath)
        {
            string[] Lines = File.ReadAllLines(FilePath);

            if (Lines.Length != 0 && Lines[0].StartsWith("// Version #"))
            {
                int DatabaseVersion = 0;
                DatabaseVersion = int.Parse(Lines[0].Substring(Lines[0].IndexOf("#") + 1));
                switch (DatabaseVersion)
                {
                    case 1:
                        string ClientVersion = Lines[2].Substring(Lines[2].IndexOf("V") + 1);
                        Globals.TargetClientVersion = ClientVersion;
                        break;
                    default:
                        NamesListView.Items.Clear();
                        this.Text = TitleText;
                        MessageBox.Show("Unsupported Database Version Format Detected!" + Environment.NewLine + "Attempt to Import the database to use with this version of Name Manager.", "Unsupported Database Version", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                }

                this.Text = $"{TitleText} | ClientVersion: {Globals.TargetClientVersion} | {(EditModeBtn.Enabled ? "Add Mode" : "Edit Mode")}";
            }
            else
            {
                NamesListView.Items.Clear();
                this.Text = TitleText;
                MessageBox.Show("Invalid Database Format Detected!", "Invalid Database", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            NamesListView.Items.Clear();

            List<NameClass> NamesObject = new NameManager().FromJSON(Lines);

            AddNamesToListView(NamesObject);
            return true;
        }

        private void UpdateCategoryAutoComplete()
        {
            List<string> CategoryItems = new List<string>();

            // Add each existing category to the available dropdown if it does not exist within it already
            foreach (ListViewItem item in NamesListView.Items)
            {
                string CategoryName = item.SubItems["Category"].Text;
                if (!CategoryItems.Contains(CategoryName))
                {
                    CategoryItems.Add(CategoryName);
                }
            }

            CategoryComboBox.Items.Clear();
            CategoryComboBox.Items.AddRange(CategoryItems.ToArray());
        }

        private bool IsDatabaseLoaded()
        {
            if (!DatabaseLoaded)
            {
                MessageBox.Show("Please create a new database or load an existing one before proceeding.", "No Database Loaded", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
        }

        private void ExportNamesBtn_Click(object sender, EventArgs e)
        {
            if (!IsDatabaseLoaded())
            {
                return;
            }

            if (NamesListView.Items.Count == 0)
            {
                MessageBox.Show("There are no Names in the database to export.", "No Names", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Sort names before exporting in case the user renamed something and it shifted the location
            SortNamesListView();

            ExportNamesForm exportNamesForm = new ExportNamesForm(ListViewToNameClass());
            exportNamesForm.ShowDialog(this);
        }

        private void ImportNamesBtn_Click(object sender, EventArgs e)
        {
            if (!IsDatabaseLoaded())
            {
                return;
            }

            ImportNamesForm importNamesForm = new ImportNamesForm();
            importNamesForm.ShowDialog(this);
        }

        private void ListItemToNameDataFields(ListViewItem item)
        {
            if (item != null)
            {
                CategoryComboBox.Text = item.SubItems["Category"].Text;
                AddressTB.Text = item.SubItems["Address"].Text;
                NameTB.Text = item.SubItems["Name"].Text;
                StatusComboBox.SelectedIndex = int.Parse(item.SubItems["Status"].Text);
                CommentTB.Text = item.SubItems["Comment"].Text;
            }
            else
            {
                CategoryComboBox.Text = "";
                AddressTB.Text = "";
                NameTB.Text = "";
                StatusComboBox.SelectedIndex = 0;
                CommentTB.Text = "";
            }
        }

        private void NamesListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (EditNameDataMode && NamesListView.SelectedItems.Count == 1)
            {
                ListItemToNameDataFields(NamesListView.SelectedItems[0]);

                CopySelectedItemBtn.Enabled = true;
                DeleteSelectedItemBtn.Enabled = true;
            }
            else if (EditNameDataMode && NamesListView.SelectedItems.Count == 0)
            {
                ListItemToNameDataFields(null);

                CopySelectedItemBtn.Enabled = false;
                DeleteSelectedItemBtn.Enabled = false;
            }
        }

        private void NewDatabaseBtn_Click(object sender, EventArgs e)
        {
            NewDatabaseForm newDatabaseForm = new NewDatabaseForm();
            if (newDatabaseForm.ShowDialog(this) == DialogResult.OK)
            {
                NamesListView.Items.Clear();
                Globals.TargetClientVersion = newDatabaseForm.ClientVersion;
                WriteNamesToFile(newDatabaseForm.DatabaseFilePath);
                if (LoadNamesFromDatabase(newDatabaseForm.DatabaseFilePath))
                {
                    DatabasePath = newDatabaseForm.DatabaseFilePath;
                    DatabaseLoaded = true;
                }
                else
                {
                    DatabasePath = "";
                    DatabaseLoaded = false;
                }
            }
        }

        private void LoadDatabaseBtn_Click(object sender, EventArgs e)
        {
            OpenFileDialog OFD = new OpenFileDialog
            {
                Title = "Select Database to load...",
                Filter = "JSON Files (*.json) | *.json|Text Files (*.txt) | *.txt",
                RestoreDirectory = true,
                FileName = DatabasePath
            };

            if (OFD.ShowDialog() == DialogResult.OK)
            {
                if (LoadNamesFromDatabase(OFD.FileName))
                {
                    DatabasePath = OFD.FileName;
                    DatabaseLoaded = true;
                }
                else
                {
                    DatabasePath = "";
                    DatabaseLoaded = false;
                }
            }
        }

        private void SaveDatabaseBtn_Click(object sender, EventArgs e)
        {
            if (!IsDatabaseLoaded())
            {
                return;
            }

            WriteNamesToFile(DatabasePath);
            MessageBox.Show($"Database saved to [{DatabasePath}]", "Database Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SaveDatabaseAsBtn_Click(object sender, EventArgs e)
        {
            if (!IsDatabaseLoaded())
            {
                return;
            }

            SaveFileDialog SFD = new SaveFileDialog
            {
                Title = "Select Database to save as...",
                Filter = "JSON Files (*.json) | *.json|Text Files (*.txt) | *.txt",
                RestoreDirectory = true,
                FileName = DatabasePath
            };

            if (SFD.ShowDialog() == DialogResult.OK)
            {
                WriteNamesToFile(SFD.FileName);
                DatabasePath = SFD.FileName;
                MessageBox.Show($"Database saved to [{DatabasePath}]", "Database Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ToggleEditInputMode(bool EditMode)
        {
            if (EditMode)
            {
                EditNameDataMode = true;
                SubmitNameBtn.Enabled = false;
                AddModeBtn.Enabled = true;
                EditModeBtn.Enabled = false;
            }
            else
            {
                EditNameDataMode = false;
                SubmitNameBtn.Enabled = true;
                EditModeBtn.Enabled = true;
                AddModeBtn.Enabled = false;
            }

            if (DatabaseLoaded)
            {
                this.Text = $"{TitleText} | ClientVersion: {Globals.TargetClientVersion} | {(EditModeBtn.Enabled ? "Add Mode" : "Edit Mode")}";
            }
        }

        private void EditModeBtn_Click(object sender, EventArgs e)
        {
            if (!IsDatabaseLoaded())
            {
                return;
            }

            ToggleEditInputMode(true);
        }

        private void AddModeBtn_Click(object sender, EventArgs e)
        {
            if (!IsDatabaseLoaded())
            {
                return;
            }

            ToggleEditInputMode(false);
        }

        private bool HasBaseAddress()
        {
            if (BaseAddressTB.Text == "")
            {
                if (MessageBox.Show("No Base Address specified. Do you want to reset to the default address 0x400000?", "No Base Address specified", MessageBoxButtons.OKCancel, MessageBoxIcon.Error) == DialogResult.Yes)
                {
                    BaseAddressTB.Text = "0x400000";
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsValidAddress(string InAddress, out int OutAddress)
        {
            if (InAddress.StartsWith("0x") && InAddress.Length > 2 && InAddress.Length < 11)
            {
                bool IsValid = Int32.TryParse(InAddress.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out OutAddress);

                if (!IsValid)
                {
                    return false;
                }
            }
            else
            {
                OutAddress = 0;
                return false;
            }
            
            return true;
        }

        private void SubmitNameBtn_Click(object sender, EventArgs e)
        {
            if (!IsDatabaseLoaded())
            {
                return;
            }

            if (!HasBaseAddress())
            {
                return;
            }

            if (IsValidAddress(BaseAddressTB.Text, out int LocalBaseAddress))
            {
                if (IsValidAddress(AddressTB.Text, out int LocalAddress))
                {
                    int OffsetAddress = LocalBaseAddress - Globals.BASEADDRESS;

                    NameClass Name = new NameClass();

                    Name.Category = CategoryComboBox.Text;
                    Name.Address = $"0x{(LocalAddress - OffsetAddress):X}";
                    Name.Name = NameTB.Text;
                    Name.Status = StatusComboBox.SelectedIndex;
                    Name.Comment = CommentTB.Text;

                    AddNamesToListView(new List<NameClass>() { Name });
                    ListItemToNameDataFields(null);
                }
                else
                {
                    MessageBox.Show("Address is invalid. Please enter a valid address offset in the format 0x########.", "Invalid Address", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                MessageBox.Show("Base Address is invalid. Please enter a valid address offset in the format 0x########.", "Invalid Base Address", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        private void CategoryComboBox_TextChanged(object sender, EventArgs e)
        {
            if (EditNameDataMode && NamesListView.SelectedItems.Count == 1)
            {
                NamesListView.SelectedItems[0].SubItems["Category"].Text = CategoryComboBox.Text;
            }
        }

        private void NameTB_TextChanged(object sender, EventArgs e)
        {
            if (EditNameDataMode && NamesListView.SelectedItems.Count == 1)
            {
                NamesListView.SelectedItems[0].SubItems["Name"].Text = NameTB.Text;
            }
        }

        private void StatusComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (EditNameDataMode && NamesListView.SelectedItems.Count == 1)
            {
                NamesListView.SelectedItems[0].SubItems["Status"].Text = StatusComboBox.SelectedIndex.ToString();
            }
        }

        private void CommentTB_TextChanged(object sender, EventArgs e)
        {
            if (EditNameDataMode && NamesListView.SelectedItems.Count == 1)
            {
                NamesListView.SelectedItems[0].SubItems["Comment"].Text = CommentTB.Text;
            }
        }

        private void UpdateSelectedItemAddress()
        {
            if (!HasBaseAddress())
            {
                return;
            }

            if (IsValidAddress(BaseAddressTB.Text, out int LocalBaseAddress))
            {
                if (IsValidAddress(AddressTB.Text, out int LocalAddress))
                {
                    int OffsetAddress = LocalBaseAddress - Globals.BASEADDRESS;

                    NamesListView.SelectedItems[0].SubItems["Address"].Text = $"0x{(LocalAddress - OffsetAddress):X}";
                }
                else
                {
                    MessageBox.Show("Address is invalid. Please enter a valid address offset in the format 0x########.", "Invalid Address", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                MessageBox.Show("Base Address is invalid. Please enter a valid address offset in the format 0x########.", "Invalid Base Address", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        private void AddressTB_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (EditNameDataMode && NamesListView.SelectedItems.Count == 1)
                {
                    UpdateSelectedItemAddress();
                }
            }
        }

        private void AddressTB_Leave(object sender, EventArgs e)
        {
            if (EditNameDataMode && NamesListView.SelectedItems.Count == 1)
            {
                UpdateSelectedItemAddress();
            }
        }

        private void CopySelectedItemBtn_Click(object sender, EventArgs e)
        {
            if (NamesListView.SelectedItems.Count == 1)
            {
                ListViewItem NameItem = NamesListView.SelectedItems[0];

                string result = "";

                result += "Category: " + NameItem.SubItems["Category"].Text + Environment.NewLine;
                result += "Address: " + NameItem.SubItems["Address"].Text + Environment.NewLine;
                result += "Name: " + NameItem.SubItems["Name"].Text + Environment.NewLine;
                result += "Status: " + NameItem.SubItems["Status"].Text + Environment.NewLine;
                result += "Comment: " + NameItem.SubItems["Comment"].Text + Environment.NewLine;

                Clipboard.SetText(result.Trim());
            }
        }

        private void DeleteSelectedItemBtn_Click(object sender, EventArgs e)
        {
            if (NamesListView.SelectedItems.Count == 1)
            {
                NamesListView.SelectedItems[0].Remove();
            }
        }

        private void NamesListContextMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            if (NamesListView.SelectedItems.Count == 0)
            {
                e.Cancel = true;
            }
        }
    }
}
