using System.Windows.Controls;

namespace SupermarketPos.Wpf.Views;

public sealed class BatchDatePicker : DatePicker
{
    public BatchDatePicker()
    {
        DateValidationError += (_, error) =>
        {
            error.ThrowException = false;
            SetCurrentValue(SelectedDateProperty, null);
        };
    }
}
