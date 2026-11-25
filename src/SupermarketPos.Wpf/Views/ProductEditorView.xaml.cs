using System.Windows.Controls;

namespace SupermarketPos.Wpf.Views;

public partial class ProductEditorView : UserControl
{
    private bool _barcodeAttached;
    public ProductEditorView()
    {
        InitializeComponent();
        Loaded+=(_,_)=>{if(!_barcodeAttached&&DataContext is ViewModels.ProductsViewModel model){model.AttachBarcodeField(BarcodeField);model.AttachBarcodeField(PackageBarcodeField,package:true);_barcodeAttached=true;}};
    }
}
