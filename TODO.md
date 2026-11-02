THINGS TO ADD / CORRECTIONS
===========================

- Analyse the possibility of, in the aircraft line, to draw waypoints from its route (small open circle with
the waypoint label name). Forward route will show a dashed line through the following waypoints. Users will provide
navdata
- Implement less opaque versions for facilities. Make sure all can be seen if piled together. Top ones should have less opacity (CTR, etc), then APP, TWR, with DEL and GND being the more opaque around 40%?
 - We are missing _OBS types in the ATC list, i never see them on map. These should be pretty transparent (~15%), white and show with a dashed line contour.
- ATC hover labels have a blank line. There are only two lines of text and a third blank line - remove it.
- When hovering over an aircraft, remove its halo and instead increase the icon size by 2px. Also remove the HEADING indication from its label.
- Aircraft icons: Add a small "shadow" below them so they have more contrast in the maps
- Change colour of the aircraft icons depending on map so that they can be easily seen in dark/light maps. White on darker maps, black or dark grey on lighter maps.
