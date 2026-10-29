// Parse only the local development values needed by face smoke tests.
// This deliberately does not expose values in logs or return error text.
export function parseLocalVars(text) {
  if (typeof text !== "string") throw new TypeError("Local vars text required.");
  const values = Object.create(null);
  for (const line of text.replace(/^\uFEFF/, "").split(/\r?\n/)) {
    if (/^\s*(?:#|$)/.test(line)) continue;
    const match = /^\s*([A-Z][A-Z0-9_]*)\s*=\s*(.*?)\s*$/.exec(line);
    if (!match) continue;
    const name = match[1];
    if (Object.hasOwn(values, name))
      throw new Error("Duplicate local variable name: " + name);
    let value = match[2];
    if (value.startsWith('"') || value.startsWith("'")) {
      const quote = value[0];
      if (value.length < 2 || !value.endsWith(quote))
        throw new Error("Unterminated quoted local variable: " + name);
      value = value.slice(1, -1);
    } else {
      // dotenv treats unquoted # as the start of an inline comment.
      value = value.replace(/\s+#.*$/, "").trimEnd();
    }
    values[name] = value;
  }
  return values;
}
