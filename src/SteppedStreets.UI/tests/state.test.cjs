// Stepped Streets. Original work by Tevin Benjamin, 2026-11. GPL-3.0.
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

test("missing, broken or non-object state keeps the tool closed with saved defaults", async () => {
    const { readState } = await stateModule();
    for (const value of [null, undefined, "", "{", "null", "[]", 12]) {
        const state = readState(value);
        assert.equal(state.stage, "Closed");
        assert.equal(state.settings.highestStep, 3.2);
        assert.equal(state.settings.narrowestWidthCells, 3);
        assert.deepEqual(state.messages, []);
    }
});

test("partial state retains defaults and preserves escaped player-facing text", async () => {
    const { readState } = await stateModule();
    const name = 'Hill "Street"\\North\n\t\u0001';
    const state = readState(JSON.stringify({ stage: "Planned", streetName: name, settings: { side: "Left" } }));
    assert.equal(state.streetName, name);
    assert.equal(state.settings.side, "Left");
    assert.equal(state.settings.depthCells, 3);
});

test("wrong types and non-finite numbers cannot enter controls or text lists", async () => {
    const { readState } = await stateModule();
    const state = readState({ stage: "Unknown", hoverGradePercent: NaN, hoverTooFlat: "false",
        messages: [null, "Kept", {}], settings: { side: "sideways", lotWidthCells: "6", bankGrade: Infinity },
        zones: [null, { name: 1 }, { name: "A", label: 1 }, { name: "A", label: "Duplicate" }] });
    assert.equal(state.stage, "Closed");
    assert.equal(state.hoverGradePercent, null);
    assert.equal(state.hoverTooFlat, false);
    assert.equal(state.settings.side, "Both");
    assert.equal(state.settings.lotWidthCells, 3);
    assert.equal(state.settings.bankGrade, 0.4);
    assert.deepEqual(state.messages, ["Kept"]);
    assert.deepEqual(state.zones, [{ name: "A", label: "A" }]);
});

test("out-of-range settings are constrained before rendering", async () => {
    const { readState } = await stateModule();
    const state = readState({ settings: { lotWidthCells: 90, depthCells: -1, earthworkLimit: 100, minimumGradePercent: 2 } });
    assert.equal(state.settings.lotWidthCells, 6);
    assert.equal(state.settings.depthCells, 2);
    assert.equal(state.settings.earthworkLimit, 8);
    assert.equal(state.settings.minimumGradePercent, 4);
});

test("status explains picking, hovering, flat streets, selection and building", async () => {
    const { readState, statusLine } = await stateModule();
    assert.equal(statusLine(readState({ stage: "Picking" })), "Click a street. Shift-click adds the next one.");
    assert.equal(statusLine(readState({ stage: "Picking", hoverGradePercent: 12 })), "12% climb");
    assert.equal(statusLine(readState({ stage: "Picking", hoverGradePercent: 2, hoverTooFlat: true })), "Too flat: under 6%");
    assert.equal(statusLine(readState({ stage: "Planned", streetName: "Hill Street" })), "Hill Street");
    assert.equal(statusLine(readState({ stage: "Building", streetName: "Hill Street" })), "Building…");
});
