using CommunityToolkit.Mvvm.ComponentModel;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Wpf.ViewModels;

public sealed partial class CustomerFieldsViewModel : ObservableObject
{
    private IReadOnlyDictionary<string, decimal> _balances = new Dictionary<string, decimal>();
    [ObservableProperty] private bool _includeDetails;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private IReadOnlyList<CustomerChoice> _existingCustomers=[];
    [ObservableProperty] private CustomerChoice? _selectedCustomer;
    partial void OnSelectedCustomerChanged(CustomerChoice? value){if(value is not null)Load(value.Customer);}
    public decimal Outstanding => Sale.CustomerIdentity(Build()) is { } key ? _balances.GetValueOrDefault(key) : 0m;

    public void SetExisting(IReadOnlyList<Sale> sales)
    {
        _balances = (from sale in sales
                     let key = sale.CustomerAccountKey ?? Sale.CustomerIdentity(sale.Customer)
                     where key is not null
                     group sale by key into account
                     select account).ToDictionary(account => account.Key, account => account.Sum(sale => sale.Outstanding));
        ExistingCustomers = sales.Where(sale => sale.Customer is not null)
            .GroupBy(sale => sale.CustomerAccountKey ?? Sale.CustomerIdentity(sale.Customer) ?? $"sale:{sale.Id}")
            .Select(account => new CustomerChoice(account.OrderByDescending(sale => sale.CreatedAt).First().Customer!))
            .OrderBy(choice => choice.Label).ToArray();
    }

    public void Load(SaleCustomer? customer)
    {
        if(customer is null)SelectedCustomer=null;
        IncludeDetails = customer is not null;
        Name = customer?.Name;
        Phone = customer?.Phone;
        Email = customer?.Email;
        Address = customer?.Address;
    }

    public SaleCustomer? Build() => IncludeDetails ? SaleCustomer.Create(Name, Phone, Email, Address) : SelectedCustomer?.Customer;
}
public sealed record CustomerChoice(SaleCustomer Customer){public string Label=>$"{Customer.Name} · {Customer.Phone??Customer.Email}";}
