// Free Workers permits 50 D1 statements per invocation.
// PUT: 1 existence lookup + 1 UPDATE + 1 DELETE + up to 45 INSERTs = 48.
const MAX_PROFILE_ITEMS = 45;
const THEMES = new Set(["AURORA", "BLUSH", "NEUTRAL"]);
const ITEM_TYPES = new Set(["SKILL", "PROJECT", "LINK"]);
const ALLOWED_PROFILE_KEYS = new Set([
  "display_name", "headline", "bio", "email", "website",
  "business_card_theme", "is_active", "items",
]);
const ALLOWED_ITEM_KEYS = new Set(["item_type", "title", "description", "url", "sort_order"]);
const asObject = (value) => value !== null && typeof value === "object" && !Array.isArray(value);

function checkedText(value, field, max, required = false) {
  if (typeof value !== "string") throw new InputError(`${field}는 문자열이어야 합니다.`);
  const text = value.trim();
  if (text.length > max || (required && text.length === 0)) {
    throw new InputError(`${field} 길이가 올바르지 않습니다. (최대 ${max}자)`);
  }
  return text;
}
function assertAllowedKeys(value, keys) {
  for (const key of Object.keys(value)) {
    if (!keys.has(key)) throw new InputError(`허용되지 않은 필드: ${key}`);
  }
}
export class InputError extends Error {}

export function validateProfile(payload) {
  if (!asObject(payload)) throw new InputError("프로필은 JSON 객체여야 합니다.");
  assertAllowedKeys(payload, ALLOWED_PROFILE_KEYS);
  const name = checkedText(payload.display_name, "display_name", 100, true);
  // PUT is a full replacement. 'items' must be sent explicitly to prevent accidental deletion.
  if (!Array.isArray(payload.items) || payload.items.length > MAX_PROFILE_ITEMS) {
    throw new InputError(`items는 최대 ${MAX_PROFILE_ITEMS}개 배열로 지정해야 합니다.`);
  }
  const optional = (key, max) => checkedText(payload[key] ?? "", key, max);
  const theme = payload.business_card_theme ?? "AURORA";
  if (!THEMES.has(theme)) throw new InputError("지원하지 않는 명함 테마입니다.");
  const active = payload.is_active ?? true;
  if (typeof active !== "boolean") throw new InputError("is_active는 true/false여야 합니다.");

  const items = payload.items.map((item) => {
    if (!asObject(item)) throw new InputError("items의 원소는 객체여야 합니다.");
    assertAllowedKeys(item, ALLOWED_ITEM_KEYS);
    if (!ITEM_TYPES.has(item.item_type)) throw new InputError("잘못된 item_type입니다.");
    const sort = item.sort_order ?? 0;
    if (!Number.isSafeInteger(sort) || sort < 0 || sort > 999999) {
      throw new InputError("sort_order는 0~999999 정수여야 합니다.");
    }
    return {
      item_type: item.item_type,
      title: checkedText(item.title, "title", 150, true),
      description: checkedText(item.description ?? "", "description", 2000),
      url: checkedText(item.url ?? "", "url", 2048),
      sort_order: sort,
    };
  });

  return {
    display_name: name,
    headline: optional("headline", 200),
    bio: optional("bio", 5000),
    email: optional("email", 320),
    website: optional("website", 2048),
    business_card_theme: theme,
    is_active: active ? 1 : 0,
    items,
  };
}

export function parsePage(searchParams) {
  // Reject malformed integers rather than silently coercing them to 0.
  const limitText = searchParams.get("limit") ?? "12";
  const offsetText = searchParams.get("offset") ?? "0";
  if (!/^[0-9]+$/.test(limitText) || !/^[0-9]+$/.test(offsetText)) {
    throw new InputError("limit/offset은 0 이상의 정수여야 합니다.");
  }
  const limit = Number(limitText);
  const offset = Number(offsetText);
  if (!Number.isSafeInteger(limit) || limit < 1 || limit > 24 ||
      !Number.isSafeInteger(offset) || offset > 100000) {
    throw new InputError("limit은 1~24, offset은 0~100000이어야 합니다.");
  }
  return { limit, offset };
}

export function validCloudId(id) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id);
}
