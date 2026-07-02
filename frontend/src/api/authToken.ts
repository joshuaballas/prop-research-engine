type AccessTokenProvider = () => Promise<string | null>;

let accessTokenProvider: AccessTokenProvider = async () => null;

export function setAccessTokenProvider(provider: AccessTokenProvider | null) {
  accessTokenProvider = provider ?? (async () => null);
}

export async function getAccessToken(): Promise<string | null> {
  return accessTokenProvider();
}
