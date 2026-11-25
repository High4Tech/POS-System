using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace SupermarketPos.Wpf.Common;

/// <summary>Editable fields open from the input itself, with a private filtered view.
/// Filtering one field must never filter another field using the same catalog.</summary>
public static class SearchDropDown
{
    public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(SearchDropDown),new PropertyMetadata(false,Changed));
    public static bool GetEnabled(DependencyObject d)=>(bool)d.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject d,bool value)=>d.SetValue(EnabledProperty,value);
    private static readonly DependencyProperty StateProperty=DependencyProperty.RegisterAttached("State",typeof(State),typeof(SearchDropDown));
    private static void Changed(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        if(d is not ComboBox box)return;
        if(e.NewValue is true){box.Loaded+=Loaded;box.Unloaded+=Unloaded;if(box.IsLoaded)Loaded(box,new());}
        else{box.Loaded-=Loaded;box.Unloaded-=Unloaded;Unloaded(box,new());}
    }
    private static void Loaded(object sender,RoutedEventArgs e){var box=(ComboBox)sender;if(box.GetValue(StateProperty) is null)box.SetValue(StateProperty,new State(box));}
    private static void Unloaded(object sender,RoutedEventArgs e){var box=(ComboBox)sender;(box.GetValue(StateProperty) as State)?.Dispose();box.ClearValue(StateProperty);}
    private sealed class State : IDisposable
    {
        private readonly ComboBox _box;private TextBox? _input;private IEnumerable? _source;private ListCollectionView? _view;private bool _changing;private string _query="";
        private readonly DependencyPropertyDescriptor _descriptor=DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty,typeof(ComboBox));
        public State(ComboBox box)
        {
            _box=box;if(box.IsEditable){box.SetCurrentValue(ComboBox.IsTextSearchEnabledProperty,false);box.SetCurrentValue(ComboBox.IsSynchronizedWithCurrentItemProperty,false);}box.ApplyTemplate();_input=box.Template.FindName("PART_EditableTextBox",box) as TextBox;
            box.PreviewMouseLeftButtonDown+=Clicked;box.DropDownOpened+=Opened;box.DropDownClosed+=Closed;
            if(_input is not null)_input.TextChanged+=Typed;
            _descriptor.AddValueChanged(box,SourceChanged);
        }
        private void Clicked(object sender,MouseButtonEventArgs e)
        {
            if(!_box.IsEditable||_input is null||!_input.IsMouseOver)return;
            _box.SetCurrentValue(ComboBox.IsDropDownOpenProperty,true);
        }
        private void Opened(object? sender,EventArgs e){if(_box.IsEditable)EnsureView();}
        private void Closed(object? sender,EventArgs e){if(_view is null)return;_changing=true;try{_query="";_view.Refresh();}finally{_changing=false;}}
        private void EnsureView()
        {
            if(_view is not null||_box.ItemsSource is not IEnumerable source||source is ICollectionView)return;
            _source=source;_view=new ListCollectionView(source.Cast<object>().ToList()){Filter=Matches};
            _changing=true;var selected=_box.SelectedItem;var text=_box.Text;
            try{_box.SetCurrentValue(ItemsControl.ItemsSourceProperty,_view);_box.SetCurrentValue(ComboBox.SelectedItemProperty,selected);_box.SetCurrentValue(ComboBox.TextProperty,text);}
            finally{_changing=false;}
            if(source is INotifyCollectionChanged changes)changes.CollectionChanged+=CollectionChanged;
        }
        private void CollectionChanged(object? sender,NotifyCollectionChangedEventArgs e){var source=_source;ResetSource();_changing=true;try{_box.SetCurrentValue(ItemsControl.ItemsSourceProperty,source);}finally{_changing=false;}EnsureView();}
        private void SourceChanged(object? sender,EventArgs e){if(!_changing)ResetSource();}
        private void ResetSource(){if(_source is INotifyCollectionChanged changes)changes.CollectionChanged-=CollectionChanged;_view=null;_source=null;_query="";}
        private string DisplayText(object item)
        {
            object? value=item;
            foreach(var part in _box.DisplayMemberPath.Split('.',StringSplitOptions.RemoveEmptyEntries))value=value?.GetType().GetProperty(part)?.GetValue(value);
            return value?.ToString()??"";
        }
        private bool Matches(object item)=>_query.Length==0||DisplayText(item).Contains(_query,StringComparison.CurrentCultureIgnoreCase);
        private void Typed(object sender,TextChangedEventArgs e)
        {
            if(_changing||!_box.IsEditable||_input?.IsKeyboardFocusWithin!=true)return;
            // A selected row populating its display name is not a new search.
            if(_box.SelectedItem is{} selected&&DisplayText(selected)==_input.Text)return;
            EnsureView();if(_view is null)return;
            _changing=true;try{var text=_input.Text;_query=text.Trim();var caret=_input.CaretIndex;_box.SetCurrentValue(ComboBox.SelectedItemProperty,null);_view.Refresh();_box.SetCurrentValue(ComboBox.TextProperty,text);_input.Text=text;_box.SetCurrentValue(ComboBox.IsDropDownOpenProperty,true);_input.CaretIndex=caret;}finally{_changing=false;}
        }
        public void Dispose()
        {
            _box.PreviewMouseLeftButtonDown-=Clicked;_box.DropDownOpened-=Opened;_box.DropDownClosed-=Closed;
            if(_input is not null)_input.TextChanged-=Typed;_descriptor.RemoveValueChanged(_box,SourceChanged);
            var original=_source;ResetSource();if(original is not null){_changing=true;try{var selected=_box.SelectedItem;_box.SetCurrentValue(ItemsControl.ItemsSourceProperty,original);if(selected is not null)_box.SetCurrentValue(ComboBox.SelectedItemProperty,selected);}finally{_changing=false;}}
        }
    }
}
