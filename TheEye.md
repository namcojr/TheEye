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

        client.DefaultRequestHeaders.Add("ApiKey", "GET_YOUR_OWN_KEY");
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

THINGS TO ADD / CORRECTIONS
===========================

- Analyze the possibility to add more detailed maps.


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
canvas in a Viewbox, rotate only the artwork clockwise by normalized heading,
and use the theme's TextBrush. OPTIONS > Map layers > Aircraft glyph size sets
the base size from 20 to 30 pixels in one-pixel steps (default 21). Saving applies
it immediately and persists `aircraftGlyphSize`; Cancel leaves the map unchanged.
Selected aircraft are 3 pixels larger. Halos and click areas grow with the glyph,
while marker centres stay anchored to their coordinates. Ground opacity remains
unchanged. Heading updates reuse the same geometry;
type changes and loaded flight plans refresh the category.


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

Settings > IVAO sign-in authorises the app as the member, which is what unlocks the
authenticated feeds (recorded tracks, per-session detail) without a consumer key.
It also works for accounts that have no API key at all.

- Credentials: request an application on https://developers.ivao.aero (or by mail to
  web@ivao.aero). You get a client id and secret, and IVAO keeps a list of the
  redirect URLs the application may use.
- Redirect: the app itself listens on http://localhost:48620/callback (loopback,
  nothing exposed to the network). Register that exact URL on the IVAO application,
  or type another registered URL in Settings.
- Scopes: `openid profile tracker`. IVAO approves scopes per application: without
  `tracker` the sign-in still works but the recorded tracks stay refused, and the
  trail notice says exactly that. The scope string is editable in Settings.
- Flow: authorisation code + PKCE (S256) to https://sso.ivao.aero/authorize, the code
  is exchanged at https://api.ivao.aero/v2/oauth/token, and the access token is
  renewed with the refresh token. Both tokens are stored in settings.json next to the
  consumer key (same trust boundary as the key always had).
- Requests: a signed-in token is sent as `Authorization: Bearer` (what WebEye does);
  the consumer key is the fallback, and it is also retried if IVAO refuses the token.
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

## RUNWAY PAVEMENT ON THE MAP (implemented)
========================================

Zooming into an airport draws its runways, so a parked aircraft sits on a strip
instead of on blank land. It is all offline, like the rest of the map.

- Table: `src/TheEye.Core/Resources/Maps/airports.txt`, embedded, about 17 500
  airports built from the public-domain OurAirports data by `tools/build-airports.py`.
  One line per airport, `ICAO|lat|lon|name|runways`, and one runway per `;` with
  `leLat,leLon,heLat,heLon,widthMetres`. `AirportReference` reads it once, indexes
  it by ICAO and keeps the records in latitude order, so a viewport scan only walks
  the band it covers.
- Drawing: `IvaoMapLayers.CreateAirports` builds one rectangle per runway from the
  two real thresholds and the published width (projected, then offset by half the
  width along the perpendicular), so the strip points the right way and stretches
  with the projection. The narrowest runway is widened to `max(resolution * 1.6, 25)`
  metres so a small field stays visible at the zoom where runways appear at all, and
  the fill is the land colour blended towards the label colour, which reads as
  asphalt on every theme. Each airport also gets its ICAO label.
- Zoom gate: runways appear from `IvaoMapLayers.RunwayZoomLevel` (12, i.e.
  `RunwayResolution`) downwards; further out the layer is emptied, so panning costs
  nothing. `MapViewModel.UpdateAirports` rebuilds the layer only when the set of
  airports in view or the zoom level changed, and it looks 25 % beyond the viewport
  so the fields just off screen are already drawn when the user pans onto them.
- Order: the airport layer is added first, under the sectors, the trail and the
  aircraft markers, so the pavement never covers a badge.


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
  wider blue disc around the tower, `_OBS` is white, and `_CTR`/`_FSS`/unknown
  keep their own discs. The contours are one pixel wide at the full facility colour;
  only the fill is see-through.
- Sizes: `DeliveryDiameter`, `GroundDiameter`, `TowerDiameter`,
  `ApproachDiameter`, `ControlDiameter`, `InformationDiameter`,
  `ObserverDiameter`, `DefaultDiameter`, `OutlineWidth` and `CircleFillAlpha` are the tuning
  variables at the top of `src/TheEye.Core/Map/AtcMarkerStyles.cs`.
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
