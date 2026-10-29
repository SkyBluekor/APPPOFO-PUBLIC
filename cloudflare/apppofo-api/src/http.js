export function json(data, status = 200, extra = {}) {
  return Response.json(data, {
    status,
    headers: {
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
      ...extra,
    },
  });
}

export function problem(status, code, message) {
  return json({ error: { code, message } }, status);
}

export function noContent() {
  return new Response(null, {
    status: 204,
    headers: { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" },
  });
}

export async function readJson(request) {
  const contentType = request.headers.get("content-type")?.toLowerCase() ?? "";
  if (!/^application\/json(?:\s*;|\s*$)/.test(contentType)) {
    return { error: problem(415, "UNSUPPORTED_MEDIA_TYPE", "application/json 요청이 필요합니다.") };
  }

  // Bound the request body without trusting Content-Length. A profile is at most 64KiB.
  const MAX_BYTES = 65536;
  const reader = request.body?.getReader();
  if (!reader) return { error: problem(400, "INVALID_JSON", "JSON 본문이 필요합니다.") };
  const chunks = [];
  let length = 0;
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      length += value.byteLength;
      if (length > MAX_BYTES) {
        await reader.cancel();
        return { error: problem(413, "PAYLOAD_TOO_LARGE", "최대 64KiB까지 허용됩니다.") };
      }
      chunks.push(value);
    }
  } catch {
    return { error: problem(400, "INVALID_BODY", "요청 본문을 읽을 수 없습니다.") };
  } finally {
    reader.releaseLock();
  }
  const body = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }
  try {
    return { value: JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(body)) };
  } catch {
    return { error: problem(400, "INVALID_JSON", "유효한 UTF-8 JSON을 입력하세요.") };
  }
}
