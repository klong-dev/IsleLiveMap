# MyIsleMap vs Isle Live Map

This comparison was captured on 2026-09-19 from the live `myislemap.com` overlay controls and the bundled Gateway offline catalog. Each overlay was enabled and disabled independently; the DOM was measured after the map settled, using visible geometry/icon elements rather than total DOM nodes.

| Overlay | MyIsleMap observed | Isle Live Map offline source | UI decision |
| --- | ---: | ---: | --- |
| Migration | 12 polygons, 12 visible labels | 12 polygons | Keep geometry only; remove labels from the map.
| Patrol | 61 visible zone geometries | 61 polygons | Keep geometry only; Vietnamese inspector label.
| Sanctuary | 7 visible geometries | 7 polygons | Keep geometry only; retain distinct palette.
| AI spawn | 52 polygons, 98 badge/image elements | 52 AI zones | Keep geometry only on map; no AI name/badge text.
| Roads & trails | 32 polylines | 32 routes / 876 points | Keep thin readable lines; no route names.
| Drinkable water | 64 blue water-tile images plus water-label text | 28 water points | Use bright blue glow visuals without water names; this is the largest remaining visual gap.
| Animals | 430 SVG resource icons | 430 resources | Match count; use transparent PNGs and a restrained dark halo.
| Plants & fungi | 245 mixed SVG/dot markers | 245 resources | Match count; preserve per-resource icon filters.
| Earth | 278 SVG resource icons | 278 resources | Match count; preserve per-resource icon filters.

## Interaction matrix

Every control was tested in the sequence `baseline -> on -> off`: migration, patrol, sanctuary, AI, roads, water, animals, plants/fungi, and earth. The source site hides the corresponding overlay group when off; the desktop renderer hides the corresponding WPF visuals and persists the choice.

The resource controls were also checked at group level and individual-resource level. The source site exposes expandable resource groups; Isle Live Map exposes Vietnamese group toggles plus Vietnamese child labels with icons.

## Intentional differences

- Map labels are intentionally absent in Isle Live Map even though the source site displays them. This is required for the cleaner tactical map requested by the player.
- Source-site water is a tiled raster overlay. The offline snapshot currently contains water label coordinates, not the source tile image geometry, so the desktop renderer must not invent river/lake boundaries from coordinates.
- The source site has a browser-only activity heatmap. It is not part of the offline catalog and is not represented as static local data.

## Runtime evidence

- Full App tests: `167/167 passed`.
- Lifecycle smoke test: opens/closes the tactical map 50 times and validates zone, AI, water, resource, persistence, and no-label geometry behavior.
- Catalog test: `3/3 passed`.
- Live game overlay baseline: `artifacts/smoke/map-baseline-all-layers.png`.
- Live game overlay toggle captures: `artifacts/smoke/map-migration-off.png`, `map-patrol-off.png`, `map-sanctuary-off.png`, `map-ai-off.png`, `map-roads-off.png`, `map-water-off.png`, `map-animals-off.png`, `map-plants-off.png`, and `map-earth-off-final.png`.
- Live child-resource capture: `artifacts/smoke/map-child-resource-off-final.png`.
- The game process remained responsive while the overlay was inspected; live telemetry showed player name/species, player count, water, heading, mission progress, and `LIVE` connection state.
