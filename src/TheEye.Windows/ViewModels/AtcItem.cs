using System.Windows.Media;
using TheEye.Core.Maps;
using TheEye.Core.Models;

namespace TheEye.Windows.ViewModels;

public sealed class AtcItem : ObservableObject
{
    private IvaoAtc _atc;
    private bool _isSelected;

    public AtcItem(IvaoAtc atc)
    {
        _atc = atc;
        Refresh();
    }

    public IvaoAtc Atc => _atc;

    public long Id => _atc.Id;

    public string Callsign => _atc.Callsign;

    public string Facility => _atc.Facility;

    public string FacilityName => FacilityPalette.DisplayName(_atc.Facility);

    public string Frequency => _atc.FrequencyText;

    public string Position => _atc.PositionText;

    public string AirportIcao => _atc.AirportIcao;

    public string AirportName => _atc.AirportName;

    public string City => _atc.City;

    public string Country => _atc.Country;

    public string Scope => _atc.ScopeLabel;

    public string SectorPoints => _atc.SectorPointsText;

    public string PositionText => _atc.Position is { } position
        ? $"{position.Latitude:0.0000}, {position.Longitude:0.0000}"
        : "—";

    public string ConnectionType => _atc.ConnectionType ?? "—";

    public bool IsMilitary => _atc.IsMilitary;

    public bool HasSector => _atc.PolygonPoints.Count >= 3;

    public Brush AccentBrush { get; private set; } = Brushes.Gray;

    public Brush AccentSoftBrush { get; private set; } = Brushes.Transparent;

    public string SearchText { get; private set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void Update(IvaoAtc atc)
    {
        _atc = atc;
        Refresh();
    }

    private void Refresh()
    {
        var color = (Color)ColorConverter.ConvertFromString(FacilityPalette.ForFacility(_atc.Facility));
        AccentBrush = Freeze(new SolidColorBrush(color));
        AccentSoftBrush = Freeze(new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)));

        SearchText = string.Join(' ', new[]
        {
            Callsign,
            FacilityName,
            Frequency,
            AirportIcao,
            AirportName,
            City,
            Country,
            Scope
        }).ToLowerInvariant();

        OnPropertyChanged(string.Empty);
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
