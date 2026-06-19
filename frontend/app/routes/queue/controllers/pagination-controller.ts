import { useCallback, useEffect, useRef, useState } from "react";

export const PAGE_SIZE_OPTIONS = [25, 50, 100];

export type FetchPageResult<TSlot> = { slots: TSlot[]; total: number };

export type Pager<TSlot> = {
    slots: TSlot[];
    setSlots: React.Dispatch<React.SetStateAction<TSlot[]>>;
    total: number;
    setTotal: React.Dispatch<React.SetStateAction<number>>;
    pageNumber: number;
    pageSize: number;
    totalPages: number;
    isLive: boolean;
    pageSizeRef: React.RefObject<number>;
    goToPage: (page: number) => void;
    changePageSize: (size: number) => void;
    scheduleBackfill: () => void;
};

export type UsePagerArgs<TSlot> = {
    initialSlots: TSlot[];
    initialTotal: number;
    storageKey: string;
    defaultPageSize: number;
    fetchPage: (start: number, limit: number) => Promise<FetchPageResult<TSlot>>;
    onError: (message: string) => void;
};

function readStoredPageSize(storageKey: string, fallback: number): number {
    if (typeof window === "undefined") return fallback;
    const raw = window.localStorage.getItem(storageKey);
    const parsed = raw ? parseInt(raw, 10) : NaN;
    return PAGE_SIZE_OPTIONS.includes(parsed) ? parsed : fallback;
}

export function usePager<TSlot>(args: UsePagerArgs<TSlot>): Pager<TSlot> {
    const { initialSlots, initialTotal, storageKey, defaultPageSize, fetchPage, onError } = args;

    const [slots, setSlots] = useState<TSlot[]>(initialSlots);
    const [total, setTotal] = useState<number>(initialTotal);
    const [pageNumber, setPageNumber] = useState<number>(1);
    const [pageSize, setPageSize] = useState<number>(defaultPageSize);

    const pageSizeRef = useRef<number>(defaultPageSize);
    pageSizeRef.current = pageSize;
    const pageNumberRef = useRef<number>(1);
    pageNumberRef.current = pageNumber;
    const backfillTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
    const requestSeq = useRef(0);

    const totalPages = Math.max(1, Math.ceil(total / pageSize));
    const isLive = pageNumber === 1;

    const load = useCallback(async (page: number, size: number) => {
        const seq = ++requestSeq.current;
        try {
            const result = await fetchPage((page - 1) * size, size);
            if (seq !== requestSeq.current) return; // a newer request superseded this one
            setSlots(result.slots);
            setTotal(result.total);
        } catch (e) {
            if (seq !== requestSeq.current) return; // stale request errored after being superseded
            onError(e instanceof Error ? e.message : "Failed to load page");
        }
    }, [fetchPage, onError]);

    const goToPage = useCallback((page: number) => {
        if (backfillTimer.current) clearTimeout(backfillTimer.current);
        setPageNumber(page);
        load(page, pageSizeRef.current);
    }, [load]);

    const changePageSize = useCallback((size: number) => {
        if (backfillTimer.current) clearTimeout(backfillTimer.current);
        if (typeof window !== "undefined") {
            window.localStorage.setItem(storageKey, String(size));
        }
        setPageSize(size);
        setPageNumber(1);
        load(1, size);
    }, [load, storageKey]);

    const scheduleBackfill = useCallback(() => {
        if (pageNumberRef.current !== 1) return;
        if (backfillTimer.current) clearTimeout(backfillTimer.current);
        backfillTimer.current = setTimeout(() => {
            if (pageNumberRef.current !== 1) return; // page changed before the backfill fired
            load(1, pageSizeRef.current);
        }, 300);
    }, [load]);

    // If the total shrank so the current page no longer exists, clamp back into range.
    useEffect(() => {
        if (pageNumber > totalPages) {
            goToPage(totalPages);
        }
    }, [totalPages, pageNumber, goToPage]);

    // On mount, reconcile the persisted page size with the loader's default size.
    // (localStorage is unavailable during SSR, so the loader renders at the
    // default size and we reconcile to the stored size here on the client.)
    useEffect(() => {
        const stored = readStoredPageSize(storageKey, defaultPageSize);
        if (stored !== defaultPageSize) {
            setPageSize(stored);
            load(1, stored);
        }
        return () => {
            if (backfillTimer.current) clearTimeout(backfillTimer.current);
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    return {
        slots, setSlots, total, setTotal,
        pageNumber, pageSize, totalPages, isLive, pageSizeRef,
        goToPage, changePageSize, scheduleBackfill,
    };
}
