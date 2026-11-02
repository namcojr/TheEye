THINGS TO ADD / CORRECTIONS
===========================

- [x] Draw all flight-plan waypoints (past and future) for active aircraft whose filed routes are available as small open circles with labels and a dashed forward route. The live summary omits route strings; signing in and selecting an aircraft loads its full flight plan and updates the map, retaining that route across traffic refreshes. Resolve airway entry and exit fixes from user-provided Navigraph navdata, displaying no intermediate airway fixes. Read the database from `%APPDATA%\TheEyeIVAO\NavData\db.s3db`; do not distribute it.


- [x] Display the aircraft route line at 1.5x its previous width, without a contour.
- [x] Enlarge airport circles and set their opacity to 20%.
- [x] Remove offline runway drawing and duplicate zoomed-in airport labels; streamed maps provide airport detail.
- [x] Remove the future route and waypoint markers when the aircraft is unselected; only the selected aircraft displays its filed route.
- [x] Enlarge departure/arrival circles by another 25% and increase their opacity from 20% to approximately 35%.
- [x] Draw a translucent connector from the last reported aircraft position to its animated position between data updates, keeping the future route attached to the moving aircraft without changing recorded telemetry.
