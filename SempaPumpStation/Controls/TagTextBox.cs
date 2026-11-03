using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TerakkiFramework.Plc;
using TerakkiFramework.Plc.Abstractions;

namespace SempaPumpStation.Controls
{
    /// <summary>
    /// A protocol-agnostic editable field bound to an <see cref="IPlcTag"/> — the modern replacement
    /// for the Winlog "Edit" object. Displays the live value formatted via <see cref="Format"/>; on
    /// Enter or focus-loss it parses the text (per the tag's <see cref="DataType"/>) and writes it
    /// back through the tag (which the connection applies). While editing, the live value does not
    /// overwrite the operator's typing.
    /// </summary>
    public class TagTextBox : TextBox
    {
        private bool _updatingFromTag;

        public TagTextBox()
        {
            VerticalContentAlignment = VerticalAlignment.Center;
            LostFocus += (s, e) => Commit();
            KeyDown += OnKeyDown;
        }

        public static readonly DependencyProperty PlcTagProperty =
            DependencyProperty.Register(nameof(PlcTag), typeof(IPlcTag), typeof(TagTextBox),
                new PropertyMetadata(null, OnTagChanged));

        public static readonly DependencyProperty FormatProperty =
            DependencyProperty.Register(nameof(Format), typeof(string), typeof(TagTextBox),
                new PropertyMetadata("0.##", (d, e) => ((TagTextBox)d).PullFromTag()));

        public IPlcTag PlcTag
        {
            get => (IPlcTag)GetValue(PlcTagProperty);
            set => SetValue(PlcTagProperty, value);
        }

        /// <summary>
        /// Optional tag the operator's value is written to instead of <see cref="PlcTag"/> — for the PLC's
        /// Hp/Ph pairs: show the PLC's accepted value (Ph), write the request (Hp).
        /// </summary>
        public static readonly DependencyProperty WriteTagProperty =
            DependencyProperty.Register(nameof(WriteTag), typeof(IPlcTag), typeof(TagTextBox), new PropertyMetadata(null));

        public IPlcTag WriteTag
        {
            get => (IPlcTag)GetValue(WriteTagProperty);
            set => SetValue(WriteTagProperty, value);
        }

        public string Format
        {
            get => (string)GetValue(FormatProperty);
            set => SetValue(FormatProperty, value);
        }

        private static void OnTagChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var t = (TagTextBox)d;
            if (e.OldValue is IPlcTag oldTag) oldTag.PropertyChanged -= t.OnTagValueChanged;
            if (e.NewValue is IPlcTag newTag) newTag.PropertyChanged += t.OnTagValueChanged;
            t.PullFromTag();
        }

        private void OnTagValueChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "TagValue") return;
            if (Dispatcher.CheckAccess()) PullFromTag();
            else Dispatcher.Invoke((Action)PullFromTag);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        private void PullFromTag()
        {
            if (PlcTag == null || IsKeyboardFocused) return;
            _updatingFromTag = true;
            try { Text = FormatValue(PlcTag); }
            finally { _updatingFromTag = false; }
        }

        private string FormatValue(IPlcTag tag)
        {
            var v = tag.TagValue;
            if (v == null) return string.Empty;
            if (IsStringType(tag.DataType)) return v.ToString();
            try
            {
                double d = System.Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d)) return string.Empty;
                return d.ToString(string.IsNullOrEmpty(Format) ? "0.##" : Format, CultureInfo.InvariantCulture);
            }
            catch { return v.ToString(); }
        }

        private void Commit()
        {
            var target = WriteTag ?? PlcTag;
            if (target == null || _updatingFromTag) return;
            if (IsReadOnly) { PullFromTag(); return; }   // locked for this user: losing focus must not write

            if (IsStringType(target.DataType))
            {
                target.TagValue = Text ?? string.Empty;
                return;
            }

            var raw = (Text ?? string.Empty).Trim().Replace(',', '.');
            if (raw.Length == 0) { PullFromTag(); return; }

            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
            {
                target.TagValue = IsIntegerType(target.DataType)
                    ? (object)(int)Math.Round(d)
                    : (object)d;
            }
            PullFromTag();
        }

        private static bool IsStringType(DataType t)
            => t == DataType.STRING || t == DataType.WSTRING || t == DataType.CHAR || t == DataType.WCHAR;

        private static bool IsIntegerType(DataType t)
        {
            switch (t)
            {
                case DataType.SINT: case DataType.USINT:
                case DataType.INT: case DataType.UINT:
                case DataType.DINT: case DataType.UDINT:
                case DataType.LINT: case DataType.ULINT:
                    return true;
                default: return false;
            }
        }
    }
}
