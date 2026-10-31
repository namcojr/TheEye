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
- Icons for the aircraft (all 360 positions for 360 angles) can be found on the same SAS Acars project in the parent folders inside resources inair (IVAO traffic) and inair2 (own aircraft) folders. After the project skeleton is working, i will provide more icons for different types of aircraft.
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

- New glyphs for aircraft types. Heavy, quad engine, super, heli, prop, t-prop
- The zoom is a bit too aggressive, the steps are too big when we scroll the wheel. Each "step" should be
one zoom level up or down
- Add more detail to the maps: City delimiters, roads, detailed airport ground structure. Do we need Open StreetMap for this?


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
  wider blue disc around the tower, and `_CTR`/`_FSS`/unknown keep their own
  smaller discs. The contours are one pixel wide at the full facility colour;
  only the fill is see-through.
- Sizes: `DeliveryDiameter`, `GroundDiameter`, `TowerDiameter`,
  `ApproachDiameter`, `ControlDiameter`, `InformationDiameter`,
  `DefaultDiameter`, `OutlineWidth` and `CircleFillAlpha` are the tuning
  variables at the top of `src/TheEye.Core/Map/AtcMarkerStyles.cs`.
- Zoom limits: `WorldMap.MinZoomLevel` (default 4) is the most zoomed out level
  the navigator offers. `WorldMap.Create` builds `OverrideResolutions` from that
  level down to `DeepestZoomLevel` and sets `OverrideZoomBounds` to
  `MinResolution … MaxZoomOutResolution`, so no gesture, button or restored
  setting can reach levels 0–3 any more. `MaxZoomOutResolution` is derived from
  the level (`MaxResolution / 2^MinZoomLevel`), so changing the level is enough.
  The home view and the default `MapResolution` of the settings use that same
  resolution.
- Badge zoom: `AtcMarkerStyles.ZoomScale(resolution)` is driven by the **zoom
  level** of the map, not by the raw resolution. `MinZoomLevel` is
  `WorldMap.MinZoomLevel`, the widest view the user can reach, and there the
  badges use `MinZoomScale` (default 1, the tuned size); `MaxZoomLevel`
  (`WorldMap.DeepestZoomLevel`, 17) uses `MaxZoomScale` (default 2), and the
  multiplier rises evenly on the levels in between. `MaxZoomScale` is therefore
  the single number that controls how much the glyphs enlarge while the map is
  zoomed in; with the current diameters and `1 → 2` the tower ring goes from
  45 px to 90 px and the ground star from 27 px to 54 px. `AtcMarkerStyles.For`
  draws each badge as one SVG and scales the whole artwork with it, so circles,
  stars, the selection ring and the hit radius grow together. The callsign font
  is capped at `MaxLabelZoom` (1.6, i.e. 9 → 14.4 px) so the labels stay in
  proportion instead of printing across the map. An empty viewport (resolution 0
  or NaN) falls back to `MinZoomScale`. The result is quantised, and
  `MapViewModel.UpdateStationZoom` rebuilds the station layer only when the
  quantised multiplier changes.
- Clicks: `AtcStationHitTest` picks the smallest ring the pointer is inside, so
  the star wins in the middle and the approach ring at the rim. Clicking the same
  airport again walks outwards through the stack, and
  `IvaoMapLayers.CreateSelectedAtc` draws an accent ring around the badge that is
  selected — `SelectionRingScale` sizes it.
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
