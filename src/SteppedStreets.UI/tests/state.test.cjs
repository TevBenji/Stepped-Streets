// Stepped Streets. Original work by TevBenji, 2026-11. GPL-3.0.
const assert = require("node:assert/strict");
const { test } = require("node:test");
const fs = require("node:fs");
const path = require("node:path");

async function stateModule() {
    const source = fs.readFileSync(path.join(__dirname, "../src/lib/state.ts"), "utf8");
    const { transpileModule, ModuleKind } = require("typescript");
    const { outputText } = transpileModule(source, { compilerOptions: { module: ModuleKind.ESNext } });
    return import(`data:text/javascript;base64,${Buffer.from(outputText).toString("base64")}`);
}

const DEFAULT_SETTINGS = {
    style: "ContourSuburb", road: "Small Road", blockDepth: 90, maxRoadGradePercent: 8, steepestBuildablePercent: 15,
    steepGround: false, zoneMix: [{ zone: "Residential Low", share: 100 }], serviceMix: [], mixPattern: "Clumps", lotWidthCells: 3, lotDepthCells: 4,
    shapeGround: true, junctions: "Staggered",
    levelLots: true, earthworkLimit: 4, bankGradePercent: 40, lotSpacing: -1, retainingWalls: false, trees: false,
    treeDensityPercent: 50, seed: 1,
};
const EMPTY_STATS = {
    roadKilometres: 0, lots: 0, zonedCells: 0, earthCubicMetres: 0, steepestRoadPercent: 0, wildPercent: 0, connected: false, trees: 0,
};

test("missing, broken or non-object state keeps the tool closed with the defaults", async () => {
    const { readState } = await stateModule();
    for (const value of [null, undefined, "", "{", "null", "[]", 12, true]) {
        const state = readState(value);
        assert.equal(state.stage, "Closed");
        assert.equal(state.corners, 0);
        assert.equal(state.areaClosed, false);
        assert.equal(state.areaSquareMetres, 0);
        assert.equal(state.summary, "");
        assert.equal(state.progress, 0);
        assert.deepEqual(state.messages, []);
        assert.deepEqual(state.zoneCounts, []);
        assert.deepEqual(state.zones, []);
        assert.deepEqual(state.roads, []);
        assert.deepEqual(state.stats, EMPTY_STATS);
        assert.deepEqual(state.settings, DEFAULT_SETTINGS);
    }
});

test("partial state keeps defaults and preserves escaped player-facing text", async () => {
    const { readState } = await stateModule();
    const name = 'Hill "Street"\\North\n\t\u0001';
    const state = readState(JSON.stringify({
        stage: "Planned", summary: name, messages: [name],
        settings: { style: "MountainLane", road: name, zoneMix: [{ zone: name, share: 40 }] },
        zones: [{ name, label: name }], roads: [{ name, label: name }], zoneCounts: [{ zone: name, lots: 3 }],
    }));
    assert.equal(state.stage, "Planned");
    assert.equal(state.summary, name);
    assert.deepEqual(state.messages, [name]);
    assert.equal(state.settings.style, "MountainLane");
    assert.equal(state.settings.road, name);
    assert.deepEqual(state.settings.zoneMix, [{ zone: name, share: 40 }]);
    assert.deepEqual(state.zones, [{ name, label: name }]);
    assert.deepEqual(state.roads, [{ name, label: name }]);
    assert.deepEqual(state.zoneCounts, [{ zone: name, lots: 3 }]);
    assert.equal(state.settings.blockDepth, 90);
    assert.equal(state.settings.seed, 1);
});

test("every number is clamped to its range", async () => {
    const { readState } = await stateModule();
    const low = readState({
        corners: -4, areaSquareMetres: -1, progress: -0.5,
        stats: { roadKilometres: -1, lots: -1, zonedCells: -1, earthCubicMetres: -1, steepestRoadPercent: -1, wildPercent: -1, trees: -1 },
        settings: { blockDepth: 10, maxRoadGradePercent: 1, steepestBuildablePercent: 2, lotWidthCells: 0, lotDepthCells: 1,
            earthworkLimit: 0, bankGradePercent: 5, lotSpacing: -9, treeDensityPercent: 0, seed: -20 },
    });
    assert.equal(low.corners, 0);
    assert.equal(low.areaSquareMetres, 0);
    assert.equal(low.progress, 0);
    assert.deepEqual(low.stats, EMPTY_STATS);
    assert.deepEqual(
        [low.settings.blockDepth, low.settings.maxRoadGradePercent, low.settings.steepestBuildablePercent, low.settings.lotWidthCells,
            low.settings.lotDepthCells, low.settings.earthworkLimit, low.settings.bankGradePercent, low.settings.lotSpacing,
            low.settings.treeDensityPercent, low.settings.seed],
        [60, 4, 8, 2, 2, 1, 20, -1, 10, 1]);

    const high = readState({
        progress: 3, stats: { wildPercent: 140 },
        settings: { blockDepth: 900, maxRoadGradePercent: 40, steepestBuildablePercent: 90, lotWidthCells: 60, lotDepthCells: 9,
            earthworkLimit: 80, bankGradePercent: 99, lotSpacing: 7, treeDensityPercent: 400, seed: 1e9 },
    });
    assert.equal(high.progress, 1);
    assert.equal(high.stats.wildPercent, 100);
    assert.deepEqual(
        [high.settings.blockDepth, high.settings.maxRoadGradePercent, high.settings.steepestBuildablePercent, high.settings.lotWidthCells,
            high.settings.lotDepthCells, high.settings.earthworkLimit, high.settings.bankGradePercent, high.settings.lotSpacing,
            high.settings.treeDensityPercent, high.settings.seed],
        [200, 12, 60, 6, 6, 12, 50, 2, 100, 99999]);
});

test("whole-number fields are rounded and in-range values pass through", async () => {
    const { readState } = await stateModule();
    const state = readState({
        corners: 4.6, stats: { lots: 85.7, zonedCells: 1240.2, trees: 12.5, roadKilometres: 2.4, earthCubicMetres: 38000.5 },
        settings: { lotWidthCells: 4.4, lotDepthCells: 4.6, lotSpacing: 0.6, seed: 4242.5, earthworkLimit: 3.5, blockDepth: 125 },
    });
    assert.equal(state.corners, 5);
    assert.equal(state.stats.lots, 86);
    assert.equal(state.stats.zonedCells, 1240);
    assert.equal(state.stats.trees, 13);
    assert.equal(state.stats.roadKilometres, 2.4);
    assert.equal(state.stats.earthCubicMetres, 38000.5);
    assert.equal(state.settings.lotWidthCells, 4);
    assert.equal(state.settings.lotDepthCells, 5);
    assert.equal(state.settings.lotSpacing, 1);
    assert.equal(state.settings.seed, 4243);
    assert.equal(state.settings.earthworkLimit, 3.5);
    assert.equal(state.settings.blockDepth, 125);
});

test("enums are validated, wrong types and non-finite numbers fall back to defaults", async () => {
    const { readState } = await stateModule();
    for (const stage of ["Closed", "Drawing", "Planning", "Planned", "Building", "Built", "Failed"])
        assert.equal(readState({ stage }).stage, stage);
    const state = readState({
        stage: "Picking", corners: "5", areaClosed: "true", areaSquareMetres: NaN, progress: Infinity, summary: 5,
        messages: [null, "Kept", {}], stats: { lots: "86", connected: 1, wildPercent: NaN },
        settings: { style: "Grid", road: 7, mixPattern: "random", blockDepth: "120", levelLots: "false", trees: 0,
            retainingWalls: 1, seed: Infinity, earthworkLimit: -Infinity },
    });
    assert.equal(state.stage, "Closed");
    assert.equal(state.corners, 0);
    assert.equal(state.areaClosed, false);
    assert.equal(state.areaSquareMetres, 0);
    assert.equal(state.progress, 0);
    assert.equal(state.summary, "");
    assert.deepEqual(state.messages, ["Kept"]);
    assert.equal(state.stats.lots, 0);
    assert.equal(state.stats.connected, false);
    assert.equal(state.stats.wildPercent, 0);
    assert.deepEqual(state.settings, DEFAULT_SETTINGS);
    assert.equal(readState({ settings: { mixPattern: "Random", style: "TerracedBlocks", trees: false } }).settings.mixPattern, "Random");
    assert.equal(readState({ settings: { steepGround: true } }).settings.steepGround, true);
    assert.equal(readState({ settings: { style: "TerracedBlocks" } }).settings.style, "TerracedBlocks");
    assert.equal(readState({ settings: { trees: false } }).settings.trees, false);
    assert.equal(readState({ stats: { connected: true } }).stats.connected, true);
});

test("zone mix keeps valid rows, drops duplicates and falls back to the default", async () => {
    const { readState } = await stateModule();
    const mix = zoneMix => readState({ settings: { zoneMix } }).settings.zoneMix;
    assert.deepEqual(mix([
        null, "Residential Low", { zone: "", share: 50 }, { zone: 4, share: 50 }, { zone: "Office", share: 0 },
        { zone: "Office", share: -5 }, { zone: "Office", share: "50" }, { zone: "Office", share: NaN }, { share: 20 },
        { zone: "Residential Medium Row", share: 60 }, { zone: "Commercial Low", share: 250 }, { zone: "Residential Medium Row", share: 10 },
        { zone: "Residential High", share: 12.5 },
    ]), [
        { zone: "Residential Medium Row", share: 60 }, { zone: "Commercial Low", share: 100 }, { zone: "Residential High", share: 12.5 },
    ]);
    for (const empty of [[], [{ zone: "Office", share: 0 }], "Residential Low", null, {}])
        assert.deepEqual(mix(empty), [{ zone: "Residential Low", share: 100 }]);
});

test("layouts, junctions and the terrain option read leniently", async () => {
    const { readState } = await stateModule();
    const good = readState({ settings: { style: "SkewedGrid", junctions: "TrafficOptimised", shapeGround: false } });
    assert.equal(good.settings.style, "SkewedGrid");
    assert.equal(good.settings.junctions, "TrafficOptimised");
    assert.equal(good.settings.shapeGround, false);
    const bad = readState({ settings: { style: "Hexagons", junctions: 3, shapeGround: "no" } });
    assert.equal(bad.settings.style, "ContourSuburb");
    assert.equal(bad.settings.junctions, "Staggered");
    assert.equal(bad.settings.shapeGround, true);
});

test("the radial, linear and branching layouts are read, and only grids offer junctions", async () => {
    const { readState, usesJunctions } = await stateModule();
    for (const style of ["DiagonalGrid", "CurvedGrid", "WarpedGrid", "RadialGrid", "Star", "Rings", "Spiderweb", "Fan",
        "Spine", "Ladder", "Comb", "Fishbone", "Tree", "CulDeSacs", "Loops", "LoopsAndLollipops", "Lollipops",
        "TriangularMesh", "Hexagonal", "HexagonalSpokes", "Ridge", "Valley", "Coastal", "Winding",
        "OrganicMesh", "Hybrid", "Polycentric", "Superblock", "Radburn", "FusedGrid", "FilteredGrid", "BarcelonaSuperblock"])
        assert.equal(readState({ settings: { style } }).settings.style, style);
    assert.equal(usesJunctions("RadialGrid"), true);
    assert.equal(usesJunctions("Fan"), false);
    assert.equal(usesJunctions("Lollipops"), false);
    assert.equal(usesJunctions("Hexagonal"), false);
    assert.equal(usesJunctions("FilteredGrid"), false);
    assert.equal(usesJunctions("ContourSuburb"), false);
});

test("catalogue icons pass through when they are text and are left out otherwise", async () => {
    const { readState } = await stateModule();
    const state = readState({
        zones: [{ name: "A", label: "A", icon: "Media/Game/Icons/ZoneResidential.svg" }, { name: "B", label: "B", icon: 7 }, { name: "C", icon: "" }],
        services: [{ name: "Police", label: "Police", icon: "Media/Game/Icons/Police.svg" }, { name: "Fire", icon: null }],
    });
    assert.deepEqual(state.zones, [{ name: "A", label: "A", icon: "Media/Game/Icons/ZoneResidential.svg" }, { name: "B", label: "B" }, { name: "C", label: "C" }]);
    assert.equal(state.services[0].icon, "Media/Game/Icons/Police.svg");
    assert.equal("icon" in state.services[1], false);
});

test("zone catalogue, roads and zone counts drop invalid and repeated rows", async () => {
    const { readState } = await stateModule();
    const state = readState({
        zones: [null, { name: 1 }, { name: "" }, { name: "A", label: 1 }, { name: "A", label: "Duplicate" }, { name: "B", label: "" }],
        roads: [{ name: "Small Road", label: "Small Road" }, { label: "No name" }, { name: "Small Road", label: "Again" }],
        zoneCounts: [{ zone: "A", lots: 12 }, { zone: "A", lots: 4 }, { zone: "B", lots: "3" }, { zone: "", lots: 1 }, { zone: "C", lots: -2 }],
    });
    assert.deepEqual(state.zones, [{ name: "A", label: "A" }, { name: "B", label: "B" }]);
    assert.deepEqual(state.roads, [{ name: "Small Road", label: "Small Road" }]);
    assert.deepEqual(state.zoneCounts, [{ zone: "A", lots: 12 }, { zone: "C", lots: 0 }]);
});

test("area reads in hectares below 100 ha and in square kilometres above", async () => {
    const { formatArea } = await stateModule();
    assert.equal(formatArea(0), "0.0 ha");
    assert.equal(formatArea(NaN), "0.0 ha");
    assert.equal(formatArea(-5), "0.0 ha");
    assert.equal(formatArea(35000), "3.5 ha");
    assert.equal(formatArea(99000), "9.9 ha");
    assert.equal(formatArea(99600), "10 ha");
    assert.equal(formatArea(350000), "35 ha");
    assert.equal(formatArea(994000), "99 ha");
    assert.equal(formatArea(996000), "1.0 km2");
    assert.equal(formatArea(1200000), "1.2 km2");
});

test("status line follows the stage and the drawing progress", async () => {
    const { readState, statusLine } = await stateModule();
    const line = value => statusLine(readState(value));
    assert.equal(line({ stage: "Drawing", corners: 0 }), "Click the ground to place corners.");
    assert.equal(line({ stage: "Drawing", corners: 2 }), "Click the ground to place corners.");
    assert.equal(line({ stage: "Drawing", corners: 3 }), "Click the first corner, or press Close area, to finish.");
    assert.equal(line({ stage: "Drawing", corners: 5, areaClosed: true, summary: "Too steep to build." }), "Too steep to build.");
    assert.equal(line({ stage: "Drawing", corners: 5, areaClosed: true }), "Area closed");
    assert.equal(line({ stage: "Planning", corners: 5 }), "Planning...");
    assert.equal(line({ stage: "Planned", summary: "86 lots" }), "Neighbourhood planned");
    assert.equal(line({ stage: "Building", progress: 0.5 }), "Building...");
    assert.equal(line({ stage: "Built" }), "Built");
    assert.equal(line({ stage: "Failed", summary: "The game refused the roads." }), "The game refused the roads.");
    assert.equal(line({ stage: "Failed" }), "Failed");
    assert.equal(line({ stage: "Closed" }), "");
});

test("services: catalogue and counts keep valid rows, clamp counts and drop repeats", async () => {
    const { readState } = await stateModule();
    const state = readState({
        services: [{ name: "Police Station", label: "Police station", group: "Police", width: 4, depth: 5 }, { name: "" }, 7,
            { name: "Police Station" }, { name: "Clinic", width: 99 }],
        serviceCounts: [{ name: "Police Station", count: 2 }, { name: "Clinic", count: 0 }],
        settings: { serviceMix: [{ name: "Clinic", count: 50 }, { name: "Clinic", count: 1 }, { name: "Park", count: NaN }] },
    });
    assert.deepEqual(state.services, [
        { name: "Police Station", label: "Police station", group: "Police", width: 4, depth: 5 },
        { name: "Clinic", label: "Clinic", group: "", width: 20, depth: 4 },
    ]);
    assert.deepEqual(state.serviceCounts, [{ name: "Police Station", count: 2 }]);
    assert.deepEqual(state.settings.serviceMix, [{ name: "Clinic", count: 20 }]);
});

test("the detail line shows the tool's step while it works and the build report after", async () => {
    const { readState, statusDetail } = await stateModule();
    const detail = value => statusDetail(readState(value));
    assert.equal(detail({ stage: "Building", summary: "Zoning Residential Low..." }), "Zoning Residential Low...");
    assert.equal(detail({ stage: "Built", summary: "Built 2.4 km of road" }), "Built 2.4 km of road");
    assert.equal(detail({ stage: "Planning", summary: "2.1 km of road planned" }), "2.1 km of road planned");
    assert.equal(detail({ stage: "Planned", summary: "86 lots" }), "");
    assert.equal(detail({ stage: "Failed", summary: "The game refused the roads." }), "");
});

test("zone colours come from name keywords", async () => {
    const { zoneColour } = await stateModule();
    const lightGreen = zoneColour("Residential Low");
    const green = zoneColour("Residential Medium Row");
    const grey = zoneColour("Unzoned");
    assert.equal(zoneColour("Residential Medium"), green);
    assert.equal(zoneColour("EU Residential Row"), green);
    const others = [lightGreen, green, zoneColour("Residential High"), zoneColour("Commercial High"), zoneColour("Office Low"),
        zoneColour("Industrial Manufacturing"), grey];
    assert.equal(new Set(others).size, others.length);
    assert.equal(zoneColour("Commercial Low"), zoneColour("Commercial High"));
    assert.equal(zoneColour("Residential Mixed"), grey);
    assert.equal(zoneColour(""), grey);
    for (const colour of others) assert.match(colour, /^#[0-9a-f]{6}$/);
});

test("thousands separators for counts", async () => {
    const { thousands } = await stateModule();
    assert.equal(thousands(0), "0");
    assert.equal(thousands(86), "86");
    assert.equal(thousands(1240), "1,240");
    assert.equal(thousands(38000.4), "38,000");
    assert.equal(thousands(1234567), "1,234,567");
});
