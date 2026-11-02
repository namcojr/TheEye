THINGS TO ADD / CORRECTIONS
===========================

- Analyse the possibility of, in the aircraft line, to draw waypoints from its route (small open circle with
the waypoint label name). Forward route will show a dashed line through the following waypoints. Users will provide
navdata. I have left an example image in the chat. Do not implement this one, only report and let me know about which navigraph data set would be required if you know. I only need waypoints on the route, extracted from the flightplan but flightplans contain airways etc which will need to be parsed to know which NEXT waypoint they end in so that when multiple points with the same name exist, the correct one is displayed. No points within the airway are to be displayed, only START waypoint => Airway and its waypoints not displayed => END waypoint.


- The aircraft route line seems to have a "contour" around it. I need the line to be displayed as 1.5x width only.