using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SupermarketPos.Domain.Business;
namespace SupermarketPos.Wpf.Views;
public sealed class InvoiceNotesPanel:StackPanel
{
    public InvoiceNotesPanel(BusinessProfile profile)
    {
        var notes=profile.ReceiptNotes.OrderBy(n=>n.Order).Select(n=>n with{}).ToList();
        void Save(){for(var i=0;i<notes.Count;i++)notes[i].Order=i;typeof(BusinessProfile).GetProperty(nameof(profile.ReceiptNotes))!.SetValue(profile,notes.ToArray());}
        void Render()
        {
            Children.Clear();
            for(var i=0;i<notes.Count;i++)
            {
                var index=i;var note=notes[i];var row=new DockPanel{Margin=new Thickness(0,3,0,3)};var controls=new WrapPanel();DockPanel.SetDock(controls,Dock.Right);row.Children.Add(controls);
                var enabled=new CheckBox{IsChecked=note.Enabled,VerticalAlignment=VerticalAlignment.Center};enabled.Checked+=(_,_)=>{note.Enabled=true;Save();};enabled.Unchecked+=(_,_)=>{note.Enabled=false;Save();};controls.Children.Add(enabled);
                var up=new Button{Content="↑",IsEnabled=i>0};up.SetResourceReference(StyleProperty,"GhostButton");up.Click+=(_,_)=>{(notes[index-1],notes[index])=(notes[index],notes[index-1]);Save();Render();};controls.Children.Add(up);
                var down=new Button{Content="↓",IsEnabled=i<notes.Count-1};down.SetResourceReference(StyleProperty,"GhostButton");down.Click+=(_,_)=>{(notes[index+1],notes[index])=(notes[index],notes[index+1]);Save();Render();};controls.Children.Add(down);
                controls.Children.Add(BusinessForm.Button("Common_Delete",(_,_)=>{notes.RemoveAt(index);Save();Render();},"DangerButton"));
                var text=new TextBox{Text=note.Text,MaxLength=300};text.TextChanged+=(_,_)=>{note.Text=text.Text;Save();};row.Children.Add(text);Children.Add(row);
            }
            var add=BusinessForm.Button("Invoice_AddNote",(_,_)=>{notes.Add(new ReceiptNote());Save();Render();},"GhostButton");add.IsEnabled=notes.Count<20;Children.Add(add);
        }
        Render();
    }
}
