# Register keyboard and Tech Store verification

The implementation and evidence are recorded in [Tech Retail and keyboard-first Sales](TECH_RETAIL_KEYBOARD_FIRST_IMPLEMENTATION.md).

| Capability | Tech Retail | Pharmacy | Grocery | General Retail |
| --- | --- | --- | --- | --- |
| Shared Sales, purchases, customers, reports | Yes | Yes | Yes | Yes |
| Exact device serial/IMEI and warranty | Product-specific | Hidden | Hidden | Hidden |
| Brand, Model and SKU editor | Yes | Hidden | Hidden | Hidden |
| Medicine reference and dispensing controls | Hidden | Yes | Hidden | Hidden |
| Tablet/strip/pack pricing | Hidden | Configured per product | Existing package policy | Existing package policy |
| Loose goods | Existing policy | Hidden | Configurable | Configurable |

The source changes cover the existing top navigation, shared shortcut registry, Sales view and model, product editor, device records, additive product metadata migration, backup compatibility, build/update scripts, and isolated tests. No historical product or invoice conversion is performed.
