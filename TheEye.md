TheEye is a new project to replicate IVAO's aircraft and ATC web application that will display all traffic online from both pilots and ATC (Air traffic controllers). Example payloads are below.

The application should run natively in windows and linux, we are currently building the windows version here and after it is working and feature-locked, we will clone the project on a linux machine to proceed with its native version implementation.

The application should be resizable, present a map described below, have some selectable themes (dark, light, blue, green, orange, etc) and map the ATC regions as well as the pilot planes flying based on supplied coordinates data from the API.

I am going to define a few basic landmarks but these are to be expanded by you in order to have an organized TODO.md file where we will have such landmarks, questions from you to me and vice-versa while keeping such file very clean and direct. This own TheEye.md can be used as logging or project memory as you see fit.

The map should use what we already implemented in an application 'SAS Acars' in this same computer, a parent repository inside /repos.

LANDMARKS / REQUISITES
======================

- Fastest code possible, either C++ or C# taking into account it needs to later run on Linux.
- Best presentation possible for Windows and Linux, using their native UI elements
- Look into the "Examples" folder. ivae_main.png shows the layout (tabbed) that should be implemented. Tabs
MAIN MAP CONTROLLERS PILOTS and STATISTICS should be implemented, the rest is not required. ivae_map shows
the old map view, where every ATC position or pilot icon (not displayed here) is clickable, then a window
will open with details regarding the position, either ATC or pilot.
- Aircraft now use original north-facing SVG glyphs, rotated by heading rather than one PNG per degree (see below).
- I can also try to find the executable for the old ivae we are rebuilding here in case you need to fetch anything from it. I am not sure but IIRC this is a VBasic.NET x86 application

- Check api.ivao.aero/docs and analyze the possibility of in-app login to process data without having an API
key.

LANDMARKS
=========

1. - Build the native application with a movable map by using the mouse (click-and-drag), fetch data from the server and display ATC polygons alongside pilot icons. Check then "modern map" image for an example.
2. - Clickable items. When clicking on an ATC window or aircraft icon, open a dialog containing detailed data for the station or aircraft: Callsign, route, aircraft icon (i will provide a source later). If an aircraft is selected, its route should be traced on map, based on API provided data.
3. - Add and maintain "friends" by its ID, i am not sure this will require user to login and i am not sure
how to handle this yet, probably through IVAO's login API.

This is all for now, please build your own landmarks as you see fit for buildable/testable by me builds.



DATA FETCHING EXAMPLES
======================

Example pilot payload can be fetched from
https://api.ivao.aero/v2/tracker/now/pilots/summary

Example ATC payload can be fetched from
https://api.ivao.aero/v2/tracker/now/atc/summary

The statistics for totals online, etc from
https://api.ivao.aero/v2/tracker/now/connections/stats

This is an example request, we have to pass some headers for it to work as
the public API.

```public static async Task<string> DownloadIvaoData(IvaoDatatypeEnum type)
{
    string url = type == IvaoDatatypeEnum.Pilot ? IVAO_PILOT_DATA : IVAO_ATC_DATA;
    try
    {
        AcarsConfiguration.WebAccessHandler.Invoke(null, true);

        HttpClientHandler handler = new()
        {
            CookieContainer = AcarsConfiguration.CookieJar,
            UseCookies = true
        };
        string path = url.Replace("https://api.ivao.aero", "");
        HttpClient client = new(handler, true);

        client.DefaultRequestHeaders.Authorization =
          new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("Origin", "https://webeye.ivao.aero");
        client.DefaultRequestHeaders.Add("authority", "api.ivao.aero");
        client.DefaultRequestHeaders.Add("method", "GET");
        client.DefaultRequestHeaders.Add("path", path);

        string response = await client.GetStringAsync(url);
        AcarsConfiguration.WebAccessHandler(null, false);

        return response;
    }
    catch
    {
        AcarsConfiguration.WebAccessHandler.Invoke(null, false);
        return null;
    }
}```

## AIRCRAFT SVG GLYPHS (implemented)
==================================

`src/TheEye.Core/Resources/Aircraft` contains thirteen original monochrome SVG
silhouettes. Each has a 64x64 viewBox,
points north, and uses `currentColor`. Smaller classes occupy less of the same
canvas; dimensions are symbolic rather than physically to scale.

`AircraftIconCatalog.Classify` uses the filed ICAO code (nested `icaoCode`, or
`aircraftId` when absent) and an offline type lookup. Payload descriptions
explicitly identifying a `Helicopter` also select that glyph. The feed supplies
model, wake category and heading, but not engine count, propulsion or placement.
Wake category cannot distinguish silhouettes: B707/DC-8 use classic quad despite
heavy wake; ATR72 uses twin turboprop. Business-jet classification describes the
airframe, not its ownership.

Heavy trijets (MD11, L101 and DC10/D10T/D10F) use two underwing engines and a
visible tail-engine bulge and larger, more rearward-swept wings for a distinct
MD-11-inspired outline. Its underwing nacelles protrude ahead of the leading
edges to retain an airliner appearance. B752/B753 use a longer, thinner
medium-twin silhouette.

The lookup is intentionally not a complete ICAO database. Unknown, unfiled and
unmapped types (including rear-engine trijets such as B722) use the Medium
Underwing Twin Jet silhouette as the requested visual fallback, not as a claim
about their actual engine layout. Extend the exact-code table in
`src/TheEye.Core/Map/AircraftIconCatalog.cs` to add mappings.

Both WPF and Avalonia parse bundled SVG path data into native vector geometry
once per category. No SVG package or rasterization is needed. Bundled SVGs must
use only untransformed filled paths and an optional title; unsupported geometry
or missing resources raise an explicit error. Marker templates preserve the 64x64
canvas in a Viewbox and rotate only the artwork clockwise by normalized heading.

The colour of that artwork follows the basemap, not the theme: a pin drawn in the
theme's text colour disappears as soon as the tiles under it have the other
brightness. `AircraftMarkerStyles` paints it white (`#FFFFFF`) on a dark basemap
and dark grey (`#1F2933`, not black, so it does not read like a hole next to the
map's own outlines) on a light one. `MapBasemap.IsDark` names the raster styles
that are dark (Dark Matter and Hybrid); for `Offline` the palette decides,
because that map is painted with the theme palette. `MapViewModel` recomputes the
colour on a style or theme switch and hands it to every pin, and new traffic is
born with it.

A second copy of the silhouette, turned with the aircraft, offset 1.5 pixels down
in screen space and painted with the theme's shadow brush, sits under the artwork
so the pin keeps a contour on a busy basemap. OPTIONS > Map layers > Aircraft
glyph size sets the base size from 20 to 40 pixels in one-pixel steps (default
25). Saving applies it immediately and persists `aircraftGlyphSize`; Cancel
leaves the map unchanged. Selected aircraft are 3 pixels larger. The pointer over
a pin grows its artwork by 2 pixels instead of drawing a halo, so the aircraft
under the pointer stands out without covering the traffic next to it; the click
area (glyph plus 5 pixels) stays with the un-hovered size and marker centres stay
anchored to their coordinates. Ground opacity remains unchanged. Heading updates
reuse the same geometry; type changes and loaded flight plans refresh the
category.


## AIRCRAFT MOVEMENT BETWEEN UPDATES (implemented)
=================================================

Both native shells update aircraft marker positions approximately 30 times per
second using the latest API ground speed (knots) and heading. Movement follows a
great circle, including at high latitudes and across the date line. This is a
display estimate, not simulator telemetry: turns, acceleration and wind-driven
track differences cannot be known until the next update.

Prediction stops at 60 seconds from the track timestamp (or receipt when no
timestamp is supplied). Cached/identical tracks do not restart that limit, and
older timestamped tracks cannot roll back the estimate. Missing heading or
missing/non-positive speed leaves the reported position stationary; moving
ground aircraft are predicted too. New positions are corrected smoothly over
one second. Map clicks, centring and hover routes follow the estimated marker.
Reported coordinates, speed, altitude and recorded flight trails remain API
data. Animation pauses while minimized and timers stop when the window closes.

## MAP WHEEL ZOOM (implemented)
==============================

Each full wheel notch moves half a zoom level up or down (10, 10.5, 11, 11.5, etc.)
on Windows and Linux with
a short, eased 200 ms transition. Rapid notches accumulate toward the next target
level rather than restarting from the partially zoomed view.
Partial/high-resolution wheel events accumulate into full notches. Scrolling
over aircraft or map overlays uses the same behaviour as scrolling over the map,
and the location under the pointer stays anchored while zooming. Zoom buttons
retain their predefined whole-level steps, and the zoom limits are unchanged.


## IVAO SIGN-IN (implemented)
==========================

Settings > IVAO sign-in authorises the app as the member, which is the only way to
access authenticated feeds such as recorded tracks and per-session details.

- Credentials: request an application on https://developers.ivao.aero (or by mail to
  web@ivao.aero). You get a client id and secret, and IVAO keeps a list of the
  redirect URLs the application may use.
- Redirect: the app itself listens on http://localhost:48620/callback (loopback,
  nothing exposed to the network). Register that exact URL on the IVAO application,
  or type another registered URL in Settings.
- Scopes: `openid profile tracker`. IVAO approves scopes per application: without
  `tracker` the sign-in still works, but unavailable authenticated values display
  as `-` and unauthorized notices stay quiet. The scope string is editable in Settings.
- Flow: authorisation code + PKCE (S256) to https://sso.ivao.aero/authorize, the code
  is exchanged at https://api.ivao.aero/v2/oauth/token, and the access token is
  renewed with the refresh token. Tokens are stored in settings.json.
- Requests: authenticated feeds use the signed-in token as `Authorization: Bearer`
  (what WebEye does); there is no API-key fallback.
- Verification: IVAO publishes a public test client in its OAuth samples repo
  (github.com/ivaoaero/OAuth-samples). Its redirect list includes
  http://localhost:8000/auth/callback, so the flow can be exercised locally; those
  credentials only authorise /v2/users/me, proving the sign-in (account label appears)
  but not the trail.


## MAP HOVER AND BUTTON THEMES
=============================

Hovering over an unselected aircraft temporarily shows its quick route together
with the departure/arrival airport dots and ICAO labels, using the same glyphs as
the selected route. Leaving the aircraft clears the preview without changing the
selection. Button content follows its control's theme foreground in both shells,
including the checked PILOTS / ATC switches and the Save settings button.

## MAIN WINDOW LAYOUT (implemented)
=================================

Both shells lay the main view out in three columns: the traffic browser, a four
pixel splitter and the map. The selection pane is not a column of its own; it
floats over the right edge of the map while something is selected.

- The traffic browser is resizable. The splitter writes the width of the column
  into `trafficColumnWidth` when the drag ends, so the next launch opens with the
  width the user left. `TheEyeSettings.MinTrafficColumnWidth` (248) and
  `MaxTrafficColumnWidth` (560) are mirrored by `MinWidth`/`MaxWidth` on the
  column; a settings file edited by hand is pulled back to the nearest limit.
  `MainViewModel.TrafficColumnWidth` owns the value (the Avalonia shell reaches
  the column through `MainGrid.ColumnDefinitions[0]`, because Avalonia does not
  generate a field for a `ColumnDefinition`).
- The selection pane floats over the right edge of the map while something is
  selected and is hidden otherwise, so picking a row or clearing the selection
  never resizes the map or shifts the geography under it. The map's zoom buttons
  step left by the width of the pane while it is open, so they stay reachable.
- The ATC hover label sizes itself to its two lines. It used to be a fixed 56
  pixels tall, which read as a third, blank line under the callsign and frequency.


## AIRPORT MARKERS ON THE MAP
============================

The map draws one circle and ICAO label per traffic airport, using the embedded
OurAirports reference data. Zooming in no longer adds offline runway geometry or
an extra airport-label layer. Selected and hovered routes keep their airport
circles but reuse the traffic layer's labels instead of adding duplicate labels.
Switch to a streamed basemap for runway and other detailed airport mapping.


## FILED FLIGHT PLAN ON THE CARD (implemented)
===========================================

The tracker summary embeds a trimmed copy of the flight plan (airports, aircraft,
route) but not the figures a pilot filed. Selecting a flight fetches the whole
filing from `/v2/tracker/sessions/{id}/flightPlans/latest`
(`SessionFlightPlanStore`, cached per session), and the card fills in:

- EET, endurance, first and (when filed) second alternate.
- Filed off-blocks next to the off-blocks the aircraft actually left at.
- People on board, the radio/navigation equipment codes of item 10a and the
  surveillance capabilities of item 10b (`TRANSPONDER CAPABILITY`, e.g. `L B1 D1`).
- The remarks of the filed plan.

The filed plan sends the equipment and transponder codes as records
(`{ "id": "E2", "name": "D-FIS ACARS" }`) while the summaries send bare codes, so
`IvaoCodeListConverter` reads both and keeps the codes. A refusal is named on the
card instead of leaving it empty: 401/403 points at the sign-in (or a personal API
key), and any other failure prints the reason.


## IVAO REQUEST POLICY (2026-11-01)
==================================

Pilot and ATC summaries remain the live traffic source. In particular, the ATC
summary supplies airport positions and sector geometry missing from the full ATC
feed. Full pilot/controller feeds enrich those records with member details; they
are not interchangeable with summaries.

- Successful summaries and statistics are reused for the configured refresh
  interval, including overlapping or repeated manual refreshes.
- Signed-in member feeds and supervisors are cached for two minutes. Signed-out
  runs do not request these feeds or authenticated selection data.
- Startup/settings changes issue one initial refresh. Automatic polling waits a
  full interval after a batch finishes, rather than catching up missed ticks.
- Each failing tracker endpoint backs off for 1, 2, 4, 8, then 10 minutes. Last
  successful data is retained and a warning indicates its next attempt. A 401
  is cached quietly for ten minutes.
- HTTP 429 pauses subsequent calls through the shared API client until the
  server's Retry-After deadline (one minute when the header is absent).
- Selected-session details reuse the live member record when present. A selected
  aircraft still needs its recorded history and expanded flight plan: neither is
  included in the summaries. Those requests remain on demand and cached.

The published Whazzup limit of one download per 15 seconds applies to the separate
`/v2/tracker/whazzup` endpoint, which this application does not use. This does not
establish a general tracker quota or certify application approval; those must be
confirmed with IVAO. Transport errors alone are not evidence of rate limiting.


## RECORDED TRAIL
================

Selecting a flight fetches its recorded track
(`/v2/tracker/sessions/{id}/tracks`). The map draws no line until IVAO has answered,
then paints the trail. The ghosted great-circle estimate of the filed route is only
drawn once the answer came back without a usable track — drawing it first made the
line jump across the map when the recorded trail replaced it. The airports of the
flight plan come from the summary feed, so they stay on screen while the track loads.

Only aircraft are traced. A control position has no flown path, so a station selection
never requests a track and only highlights the sector polygon or the badge
(`IvaoMapLayers.CreateSelectedAtc`). IVAO answers the tracks endpoint for ATC sessions
as well, which is what used to draw an aircraft-like line across a controller's
airport: `MainViewModel.RefreshSelectionTracksAsync` now asks for a track only while
`MapViewModel.SelectedPilot` is set, and `MapViewModel.RebuildRouteLayer` paints an
empty route layer for a station.

Filed route waypoints are resolved from a user-provided Navigraph AIRAC
`db.s3db` (`NG_FWDFD` format, with enroute waypoint and airway tables), read
read-only from the app-data directory at `TheEyeIVAO\NavData\db.s3db` (for example,
`%APPDATA%\TheEyeIVAO\NavData\db.s3db` on Windows). The database is not included
with the application or in source control; users must obtain and place their own
copy there. The app uses the airway sequence to resolve route entry and exit fixes,
omits intermediate airway fixes, draws all filed waypoints as open, labelled
circles for every aircraft, and draws each aircraft's remaining route as a dashed
line. Route strokes have no outline pen. Without usable navdata, route waypoints
and forward routes are simply omitted; the selected aircraft's flown trail
continues to work as before.


## APPLICATION ICON (implemented)
=================================

`TheEye.ico` in the repository root is the one source of the artwork. The project
takes it twice: `<ApplicationIcon>` in `src/TheEye.Windows/TheEye.Windows.csproj`
embeds it in the executable, so Explorer, the taskbar shortcut and the file properties
show it, and a linked `<Resource Link="Assets\TheEye.ico" />` packs it into the
assembly for `MainWindow.Icon`, which is what the window, the taskbar button and
Alt+Tab use. Replacing the icon therefore means replacing that single file; the drawn
eye glyph in the title bar is part of the `WindowChrome` header and is unaffected.


## ATC BADGES (implemented)
==========================

A control position that publishes no sector polygon is drawn as a badge on its
airport coordinate. All positions of one airport sit on that same coordinate and
overlap, so the smallest facility ends up in the middle and every wider facility
wraps the one before it:

- Shapes: `_DEL` and `_GND` are four point stars — `_DEL` is rotated 45° so the
  two read differently — `_TWR` is the red disc around them, `_DEP`/`_APP` the
  wider blue disc around the tower, `_OBS` is a white disc with a dashed contour,
  and `_CTR`/`_FSS`/unknown keep their own discs. The contours are one pixel wide at
  the full facility colour; only the fill is see-through.
- Fill alpha follows the facility, because the wider rings are painted on top of
  the smaller ones. `GroundFillAlpha` (102, ~40%) is the most opaque, then
  `TowerFillAlpha` (82, ~32%), `ApproachFillAlpha` (64, ~25%),
  `InformationFillAlpha`/`DefaultFillAlpha` (51, ~20%) and `ControlFillAlpha`
  (38, ~15%). Sector polygons use the same numbers through
  `AtcMarkerStyles.FillAlpha`, plus `DarkSectorFillBoost` (12) on a dark basemap,
  so a controller looks the same whether the feed published a shape or only a
  coordinate, and a pile of concentric badges still shows every contour and the
  star in the middle.
- Observers: IVAO reports the position of the station being watched along with
  the connection, so a member who only watches would have been listed and drawn
  as that controller. `IvaoAtc.IsObserver` checks the connection type, the
  `_OBS` callsign suffix and the position token; `Facility` then reports `OBS`,
  the ATC list prints "Observer" and the glyph is the white
  `ObserverFillAlpha` (38, ~15%) disc with a dashed contour
  (`ObserverDashLength`, 3 screen pixels). The dashed ring is what keeps an
  observer recognisable where the faint fill all but disappears.
- Sizes: `DeliveryDiameter`, `GroundDiameter`, `TowerDiameter`,
  `ApproachDiameter`, `ControlDiameter`, `InformationDiameter`,
  `ObserverDiameter`, `DefaultDiameter`, `OutlineWidth` and the fill alphas above
  are the tuning variables at the top of
  `src/TheEye.Core/Map/AtcMarkerStyles.cs`.
- Zoom limits: `WorldMap.MinZoomLevel` (default 3) is the most zoomed out level
  the navigator offers. `WorldMap.Create` builds `OverrideResolutions` from that
  level down to `DeepestZoomLevel` and sets `OverrideZoomBounds` to
  `MinResolution … MaxZoomOutResolution`, so no gesture, button or restored
  setting can reach levels 0–2 any more. `MaxZoomOutResolution` is derived from
  the level (`MaxResolution / 2^MinZoomLevel`), so changing the level is enough.
  The home view and the default `MapResolution` of the settings use that same
  resolution.
- Badge zoom: `AtcMarkerStyles.ZoomScale(resolution)` uses inverse viewport
  resolution, so sizes track map geometry continuously (doubling per zoom level).
  `ReferenceZoomLevel` (6) sets the level where the configured facility diameters
  apply. `MinStarDiameter`/`MaxStarDiameter` and
  `MinCircleDiameter`/`MaxCircleDiameter` set the shared screen-size limits.
  Each shape group clamps its multiplier together, preserving the different
  facility sizes instead of collapsing concentric circles to identical sizes.
  Sizes stop changing outside those limits; move `ReferenceZoomLevel` to change
  where growth happens. The shared `AtcGlyphRenderer` is registered in both
  shells and draws vectors on every frame, without SVG loading or layer rebuilds.
  Glyph contours remain `OutlineWidth` (1) screen pixel; selection/supervisor
  contours remain twice that. Callsigns stay at 9 pixels, with constant line
  spacing and a live offset below the widest badge. Hit radii use the same
  current viewport resolution.
- Date-line sectors: IVAO polygon longitudes in 0–360 notation are normalized
  without losing vertices. Adjacent vertices are unwrapped before projection;
  crossing sectors have complete translated copies at both world edges.
  Labels, centroids, selection outlines and click geometry use the same
  date-line-aware calculations, avoiding false edges across the world.
- Clicks: `AtcStationHitTest` picks the smallest ring the pointer is inside, so
  the star wins in the middle and the approach ring at the rim. Clicking the same
  airport again walks outwards through the stack, and
  `IvaoMapLayers.CreateSelectedAtc` draws an accent ring around the badge that is
  selected — `RingScale` sizes it.
- Labels: the callsigns cannot fan out sideways any more, so they are stacked
  below the widest badge of the airport, one line per facility, smallest first
  (`AtcMarkerStyles.StackedLabelOffset`).


dotnet publish src\TheEye.Windows\TheEye.Windows.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o dist\TheEye-win-x64-single

## LINUX INSTALL (implemented)
=============================

`tools/install-linux.sh` publishes the Avalonia shell and installs it into the
user profile, so the launcher picks it up without touching any system directory:

- `~/.local/opt/the-eye` — `dotnet publish src/TheEye.Desktop -c Release -r linux-x64
  --self-contained false`. Framework-dependent on purpose: the .NET 10 runtime already
  sits in `/usr/share/dotnet`, so the payload is ~32 MB instead of ~250 MB. The
  distro's `/usr/bin/dotnet` is runtime-only, so the script resolves the SDK from
  `$DOTNET` first, then `~/.dotnet/dotnet`.
- `~/.local/share/icons/hicolor/<16…512>/apps/the-eye.png` — the 256 px frame of
  `TheEye.ico`, resized with ImageMagick. `.ico` is a Windows format; the freedesktop
  icon theme only understands PNG/SVG. `gtk-update-icon-cache` is skipped: the user
  hicolor tree has no valid cache on this machine and GTK scans the folders when the
  cache is absent.
- `~/.local/bin/the-eye` — the wrapper the entry and the terminal share;
  `THE_EYE_DIR` repoints it at another payload.
- `~/.local/share/applications/the-eye.desktop` — `Name=The Eye of IVAO`,
  `Icon=the-eye`, `Categories=Network;`, `StartupWMClass=TheEye.Desktop` (the single
  WM_CLASS Avalonia sets on X11/XWayland, so taskbars group the window with the entry).

`tools/install-linux.sh --no-build` only rewrites the launcher files; re-run the
script after UI changes to ship a new build. Settings keep living in
`~/.config/TheEyeIVAO/settings.json` and crashes in `~/.config/TheEyeIVAO/crash.log`,
independently of where the payload was installed.


## LISBON OFFLINE VECTOR SAMPLE (evaluation only)
================================================

`tools/sample-lisbon-map.py` queries only major roads in a Lisbon bounding box,
the Lisboa municipal boundary (Portugal's `admin_level=7`), and mapped structures
inside the LPPT aerodrome areas. It does not download a country extract and does
not change the application's map. Python's standard library is sufficient:

```powershell
python tools\sample-lisbon-map.py --target .scratch\lisbon-vector-sample
python tools\sample-lisbon-map.py --target .scratch\lisbon-vector-sample --reuse
python -B -m unittest discover -s tools -p test_sample_lisbon_map.py
```

The second command rebuilds the sample entirely offline. Open `preview.html` in
the output directory to switch between city/airport views and the Dark/Light map
colours from `ThemeCatalog`. Both themes use identical geometry; changing colour
requires no new map download. These SVGs demonstrate theming, not Mapsui runtime
rendering. Relations are preserved as member outlines, not filled multipolygons.

Measured on 2026-11-01, without geometry simplification:

- Actual successful HTTP response body: 892,548 bytes, gzip encoded (~872 KiB).
  This is the Overpass response, not the smaller final GeoJSON. Headers/TLS and
  earlier unsuccessful evaluation requests are not included.
- Complete compressed GeoJSON: ~284 KiB; uncompressed: ~1.58 MiB.
- Major roads: 4,559 OSM features, ~242 KiB compressed as a separate layer.
- Airport features: 404 features, ~23 KiB compressed; buildings: 107, ~8 KiB.
- Municipal boundary: one Lisboa relation, ~7 KiB compressed.
- Airport detail includes 130 taxiway features, 18 aprons, 13 terminal features,
  12 hangars, gates and parking positions. Counts represent OSM features, not
  distinct physical objects: runway areas, centre lines and thresholds overlap.

The small final airport/boundary layers suit locally themed overlays. Roads
dominate this sample and should be generalized by zoom for a streaming solution.
Public Overpass had timeouts during evaluation and is not a production tile
service. An initial broad query hit the tool's 8 MiB response cap and was stopped;
building selection was then restricted to the airport area. No completeness or
navigation-accuracy guarantee applies. OSM attribution and ODbL compliance are
required. `analysis.json` records the source timestamp, sizes and limitations.

Production direction remains undecided: streamed raster tiles with temporary
caching use provider-supplied colours; streamed vector tiles or targeted vector
overlays can use the application's local palettes. This experiment persists its
requested sample outputs only; it does not add a persistent runtime tile cache.

## ONLINE BASEMAP (implemented, evaluation only)

The map can draw a raster basemap under the traffic. It is a diagnostic feature:
the bundled Natural Earth map stays the default (`Offline`), and every other
choice is a MapTiler style streamed through the proxy WebEye uses. No key ships
with the app, and the tiles belong to IVAO/MapTiler, so this is for local use
while comparing themes, not for distribution.

Settings > Map style lists `Offline` and nine styles: Basic, Bright, Liberty,
International, Positron, Voyager, Dark Matter, Toner and Hybrid.

### Endpoint and request shape

```
https://maps-maptiler.ivao.aero/api/maps/{style}/{z}/{x}/{y}.webp
```

Measured on 2026-11-02: without a `Referer` of `https://webeye.ivao.aero/` the
proxy answers `301` instead of a tile; with it the tile comes back `200`
`image/webp`, 512x512, RIFF/VP8. A style name alone is enough to tell the
styles apart (`basic`, `osm-bright`, `osm-liberty`, `openstreetmap`, `positron`,
`voyager`, `dark-matter`, `toner`, `satellite-hybrid`); no key is involved.
`HttpTileSource` therefore sets the referrer and `TheEye/1.0 (...)` on every
request, because BruTile refuses to send one that names no application.

### 512 px tiles against a 256 px zoom ladder

`WorldMap` builds its zoom levels from `MaxResolution / 2^level`, the 256 px
ladder. The proxy serves 512 px tiles, so its level `z` covers the same ground
as view level `z + 1`: the schema uses `UnitsPerPixel = MaxResolution / 2^(level + 1)`
with `tileWidth = tileHeight = 512`, which makes `{z}` in the URL equal the view
level minus one. Levels 2 to 14 are the ones the navigator asks for; the schema
carries 0 to 14 and the extents of the pannable world, north row first, so the
tiles line up with the offline geometry. A Lisbon mosaic fetched through the
schema (view level 12, z11, x972/y784) lands on the city itself.

### Caching and fallback

`FileCache` under `%AppData%\TheEyeIVAO\tilecache\<style>`, seven-day expiry, so
a tile is refetched after a week. BruTile's cache has no size limit, so the
folder is trimmed to 256 MB (oldest first) whenever a style is switched on. A
style hides the `Countries` and `Country labels` layers instead of removing
them, so `Offline` brings the bundled map back instantly. The layer carries the
attribution `(c) MapTiler (c) OpenStreetMap contributors`; the map itself still
has no attribution widget, which must be added before this is ever shown to
anyone else.
