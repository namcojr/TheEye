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

- Please check Examples/A330 Speed.PNG in order to know what i'd like to see on the pilot's card. Flags and logos are a long long shot (can this be done?) but the time online, software, simulator in use, speed, altitude, callsign, DEP and ARR airports as well as pilotID and HOME, etc would be interesting. api.ivao.aero/v2/tracker/sessions/<sessionid> returns all required data for this.

- In settings, disable IVAO SIGN IN when signed in.

- MAP specific corrections:
a) Add more map detail (at least airport runways), so that when we zoom-in, at least the airport detail can be seen.

- Make these focused fixes to the fallback glyphs (I will test everything visually - Do not test, only implement):
    a) Concerning the _DEL facility, rotate the shape 45% so it is distinguishable from the existing _GND facility.
    b) These SVG glyphs must zoom-in with the map like other _CTR facilities already do, maintaining its centre on the airport they belong to.
    c) The _TWR circle must be much wider, drawn in a similar fashion to the _CTR facilities. Red shade.
    d) The _DEP, _APP circles must be in a blue shade and slightly wider than the _TWR circles.
    e) The contour of _DEP _ARR _TWR and _GND facilities should be only 1px wide and 0% transparent using the same colour of the facility, in a similar fashion to the _CTR ones.
    f) The _DEL, _GND, _TWR, _DEP and _ARR facilities must all be centred in the airport coordinates, overlapping. Clicking on them will cycle the selected facility. The _DEL and _GND facilities are the smallest items, then the _TWR a circle whose diameter covers the previous facilities and finally _DEP and _ARR have an even larger diameter to cover the _TWR facility a bit more. Use variables for these diameters and let me know where they are so i can adjust manually.
    g) After these implementations i will look at the result and let you know if further fixes are required.

    Do ask questions if you have any


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


## RECORDED TRAIL
================

Selecting a flight or a station fetches its recorded track
(`/v2/tracker/sessions/{id}/tracks`). The map draws no line until IVAO has answered,
then paints the trail. The ghosted great-circle estimate of the filed route is only
drawn once the answer came back without a usable track — drawing it first made the
line jump across the map when the recorded trail replaced it. The airports of the
flight plan come from the summary feed, so they stay on screen while the track loads.


dotnet publish src\TheEye.Windows\TheEye.Windows.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o dist\TheEye-win-x64-single
