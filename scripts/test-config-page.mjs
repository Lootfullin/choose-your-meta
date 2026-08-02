import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const html = fs.readFileSync(
    new URL('../Configuration/configPage.html', import.meta.url),
    'utf8');
const functionSource = html.match(
    /function artworkPreference\(value, fallback\) \{[\s\S]*?\n            \}/)?.[0];
assert.ok(functionSource, 'artworkPreference helper is missing');
const artworkPreference = vm.runInNewContext(
    `${functionSource}; artworkPreference`);

assert.equal(artworkPreference('RussianFirst', '1'), '0');
assert.equal(artworkPreference('EnglishFirst', '0'), '1');
assert.equal(artworkPreference('Disabled', '0'), '2');
assert.equal(artworkPreference(0, '1'), '0');
assert.equal(artworkPreference('1', '0'), '1');
assert.equal(artworkPreference(null, '1'), '1');
assert.equal(artworkPreference('unexpected', '1'), '1');

const selectIds = [
    'ForeignMoviePosterPreference',
    'ForeignMovieLogoPreference',
    'RussianMoviePosterPreference',
    'RussianMovieLogoPreference',
    'CollectionPosterPreference',
    'CollectionLogoPreference'
];
for (const id of selectIds) {
    const select = html.match(new RegExp(
        `<select id="${id}"[\\s\\S]*?<\\/select>`))?.[0];
    assert.ok(select, `${id} select is missing`);
    for (const value of ['0', '1', '2']) {
        assert.match(select, new RegExp(`<option value="${value}">`));
    }
}

console.log('Configuration enum round-trip checks passed.');
