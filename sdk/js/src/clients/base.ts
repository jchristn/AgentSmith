import type { EnumerationQuery, EnumerationResult, Json } from "../models.js";
import { QueryParams, seg, Transport } from "../transport.js";

/** Convert an optional filter object into query parameters. */
export function toQuery(filter?: object): QueryParams | undefined {
  return filter as QueryParams | undefined;
}

/** List, get, create, update, and delete for a collection; subclasses choose the collection path. */
export abstract class CollectionClient<T = Json, TRequest = Json> {
  constructor(protected readonly t: Transport) {}

  /** Collection path, for example /v1.0/api/tenants/{tenantId}/users or /v1.0/api/resources. */
  protected abstract base(): string;

  /** Item path. */
  protected item(id: string): string {
    return `${this.base()}/${seg(id)}`;
  }

  /** List records (maxResults, skip, ordering, search, activeOnly, includeBuiltIn, plus route filters). */
  list(query?: EnumerationQuery): Promise<EnumerationResult<T>> {
    return this.t.request("GET", this.base(), { query: toQuery(query) });
  }

  /** Read a record. */
  get(id: string): Promise<T> {
    return this.t.request("GET", this.item(id));
  }

  /** Create a record. */
  create(body: TRequest): Promise<T> {
    return this.t.request("POST", this.base(), { body });
  }

  /** Update a record. */
  update(id: string, body: TRequest): Promise<T> {
    return this.t.request("PUT", this.item(id), { body });
  }

  /** Delete a record. */
  async delete(id: string): Promise<void> {
    await this.t.request("DELETE", this.item(id));
  }

  /** Iterate every record across pages. */
  async *all(query?: EnumerationQuery): AsyncGenerator<T> {
    let skip = query?.skip ?? 0;
    const maxResults = query?.maxResults ?? 100;
    while (true) {
      const page = await this.list({ ...(query ?? {}), skip, maxResults });
      for (const item of page.objects) yield item;
      if (page.endOfResults || page.objects.length === 0) return;
      skip += page.objects.length;
    }
  }
}

/** Collection under /v1.0/api/tenants/{tenantId}/{segment} (identity and admin routes). */
export class TenantCollection<T = Json, TRequest = Json> extends CollectionClient<T, TRequest> {
  constructor(t: Transport, protected readonly segment: string) {
    super(t);
  }

  protected base(): string {
    return this.t.tenantPath(`/${this.segment}`);
  }
}

/** Collection at a flat path with the tenant taken from the caller's token (Hugo domain routes). */
export class FlatCollection<T = Json, TRequest = Json> extends CollectionClient<T, TRequest> {
  constructor(t: Transport, protected readonly path: string) {
    super(t);
  }

  protected base(): string {
    return this.path;
  }
}
