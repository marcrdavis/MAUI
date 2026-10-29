// Render the small production partial for a chosen native role in static Edge fixtures.
// Real Razor role visibility is covered separately by the server rendering tests.
module.exports = (markup, partial, admin = false) => markup.replace(/<partial name="_ObjectCreationResourceGroup" model="@\("([^"]+)"\)"\s*\/>/g, (_match, type) => {
    if (admin) return '';
    return partial.replace(/^@(?:inject|model)[^\n]*\n/gm, '')
        .replace(/@if \(ObjectCreationSession\.Get\(\)\?\.Info\?\.IsAdmin == false\)\s*\{\s*/, '')
        .replace(/\}\s*$/, '').replace('@Model', type);
});
