-- Copyright (c) Honua. All rights reserved.
-- Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

BEGIN;

-- Match the live routing stops with one connected, bidirectional road chain.
INSERT INTO public.ways_vertices_pgr (id, the_geom)
SELECT id, ST_SetSRID(ST_MakePoint(longitude, latitude), 4326)
FROM (VALUES
    (1, -157.8583, 21.3069),
    (2, -157.8037, 21.2810),
    (3, -157.8600, 21.3000),
    (4, -157.8400, 21.3100),
    (5, -157.8200, 21.3200),
    (6, -157.8500, 21.3000),
    (7, -157.8000, 21.2800)
) AS nodes(id, longitude, latitude)
ON CONFLICT (id) DO UPDATE SET the_geom = EXCLUDED.the_geom;

INSERT INTO public.ways (gid, source, target, name, cost, reverse_cost, the_geom)
SELECT edges.id, source.id, target.id, 'mobile-live-road', 1, 1,
       ST_MakeLine(source.the_geom, target.the_geom)
FROM (VALUES
    (1, 3, 6),
    (2, 6, 1),
    (3, 1, 4),
    (4, 4, 5),
    (5, 5, 2),
    (6, 2, 7)
) AS edges(id, source_id, target_id)
JOIN public.ways_vertices_pgr source ON source.id = edges.source_id
JOIN public.ways_vertices_pgr target ON target.id = edges.target_id
ON CONFLICT (gid) DO UPDATE SET
    source = EXCLUDED.source,
    target = EXCLUDED.target,
    name = EXCLUDED.name,
    cost = EXCLUDED.cost,
    reverse_cost = EXCLUDED.reverse_cost,
    the_geom = EXCLUDED.the_geom;

COMMIT;
