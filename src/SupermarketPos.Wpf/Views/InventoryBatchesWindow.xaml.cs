using System.Windows;
using SupermarketPos.Domain.Entities;
using SupermarketPos.Wpf.Localization;

namespace SupermarketPos.Wpf.Views;

public partial class InventoryBatchesWindow : Window
{
    public InventoryBatchesWindow(Product product,Func<int,string,bool,Task>? setHold=null)
    {
        InitializeComponent();
        var text = LocalizationManager.Instance;
        var today = DateOnly.FromDateTime(DateTime.Today);
        void Reload()
        {
          var selected=BatchesTable.SelectedValue;var batches=product.Batches.OrderBy(b=>b.ExpiryDate).ThenBy(b=>b.Lot).ToArray();var next=batches.FirstOrDefault(b=>b.Quantity>0&&b.ExpiryDate>=today&&b.HoldReason.Length==0);
          DataContext = new
          {
            product.Name,
            Summary = text.Format("Batch_Summary", product.StockQuantity, batches.Length,
                next?.ExpiryDate.ToString("yyyy-MM-dd") ?? "—", product.UnassignedStock),
            Rows = batches.Select(batch => new
            {
                Batch = batch,
                Status = text[batch.HoldReason.Length>0?"Pharmacy_BatchHeld":batch.Quantity == 0 ? "Batch_Depleted" : batch.ExpiryDate < today ? "Batch_ExpiredStatus" :
                    batch.ExpiryDate <= today.AddDays(30) ? "Batch_ExpiringSoon" : "Batch_Ready"]
            }).ToArray()
          };BatchesTable.SelectedValue=selected;
        }
        Reload();HoldControls.Visibility=setHold is null?Visibility.Collapsed:Visibility.Visible;
        async Task Save(bool hold)
        {
            if(setHold is null)return;HoldError.Text="";
            try{if(BatchesTable.SelectedValue is not int id)throw new SupermarketPos.Domain.Common.DomainException("Pharmacy_SelectBatch");IsEnabled=false;await setHold(id,HoldReason.Text,hold);HoldReason.Clear();Reload();}
            catch(Exception ex){HoldError.Text=text[ex is SupermarketPos.Domain.Common.DomainException?ex.Message:"Pharmacy_SaveError"];}
            finally{IsEnabled=true;}
        }
        HoldButton.Click+=async(_,_)=>await Save(true);ReleaseButton.Click+=async(_,_)=>await Save(false);
    }
}
