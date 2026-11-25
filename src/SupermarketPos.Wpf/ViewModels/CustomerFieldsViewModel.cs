using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class CustomerFieldsViewModel : ObservableObject
{
    [ObservableProperty] private bool _includeDetails;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private IReadOnlyList<CustomerChoice> _existingCustomers=[];
    [ObservableProperty] private CustomerChoice? _selectedCustomer;
    partial void OnSelectedCustomerChanged(CustomerChoice? value){if(value is not null)Load(value.Customer);}
    public void SetExisting(IEnumerable<Sale> sales)=>ExistingCustomers=sales.Where(s=>s.Customer is not null)
        .GroupBy(s=>s.CustomerAccountKey??$"{s.CustomerPhone}|{s.CustomerEmail}|{s.CustomerName}")
        .Select(g=>new CustomerChoice(g.OrderByDescending(s=>s.CreatedAt).First().Customer!)).OrderBy(c=>c.Label).ToArray();

    public void Load(SaleCustomer? customer)
    {
        if(customer is null)SelectedCustomer=null;
        IncludeDetails = customer is not null;
        Name = customer?.Name;
        Phone = customer?.Phone;
        Email = customer?.Email;
        Address = customer?.Address;
    }

    public SaleCustomer? Build() => IncludeDetails ? SaleCustomer.Create(Name, Phone, Email, Address) : null;
}
public sealed record CustomerChoice(SaleCustomer Customer){public string Label=>$"{Customer.Name} · {Customer.Phone??Customer.Email}";}
