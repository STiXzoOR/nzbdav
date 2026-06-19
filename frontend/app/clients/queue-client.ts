import { withUrlBase } from "~/utils/url-base";
import type { HistorySlot, QueueSlot } from "~/clients/backend-client.server";

export type QueuePage = { slots: QueueSlot[]; total: number };
export type HistoryPage = { slots: HistorySlot[]; total: number };

export async function fetchQueuePage(start: number, limit: number): Promise<QueuePage> {
    const url = withUrlBase(`/api?mode=queue&start=${start}&limit=${limit}`);
    const response = await fetch(url);
    if (!response.ok) {
        throw new Error(`Failed to fetch queue page (status ${response.status})`);
    }
    const data = await response.json();
    return { slots: data.queue?.slots ?? [], total: data.queue?.noofslots ?? 0 };
}

export async function fetchHistoryPage(start: number, pageSize: number): Promise<HistoryPage> {
    const url = withUrlBase(`/api?mode=history&start=${start}&pageSize=${pageSize}`);
    const response = await fetch(url);
    if (!response.ok) {
        throw new Error(`Failed to fetch history page (status ${response.status})`);
    }
    const data = await response.json();
    return { slots: data.history?.slots ?? [], total: data.history?.noofslots ?? 0 };
}

export async function addNzbUrl(nzbUrl: string, category: string): Promise<string> {
    const url = withUrlBase(
        `/api?mode=addurl&name=${encodeURIComponent(nzbUrl)}`
        + `&cat=${encodeURIComponent(category)}&priority=0&pp=0`
    );
    const response = await fetch(url, { method: "POST" });
    const data = await response.json().catch(() => ({}));
    if (!response.ok || data.status === false) {
        throw new Error(data.error || `Failed to add url (status ${response.status})`);
    }
    if (!data.nzo_ids || data.nzo_ids.length !== 1) {
        throw new Error("Failed to add url: unexpected response");
    }
    return data.nzo_ids[0];
}
