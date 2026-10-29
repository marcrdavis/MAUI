'use strict';
// Resolve the production icon partial for rendered Razor fixtures, rather than placeholder boxes.
module.exports = (markup, partial) => markup.replace(/<partial name="_Icon" model="@\("([^"]+)"\)" \/>/g, (_, name) => {
    const line = partial.split('\n').find(value => value.includes(`icon == "${name}"`));
    const svg = line?.match(/<svg[\s\S]*<\/svg>/)?.[0];
    if (!svg) throw new Error('Missing shared icon: ' + name);
    return svg;
});
