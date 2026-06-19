import type { Route } from "./+types/route";
import styles from "./route.module.css"
import { Alert } from 'react-bootstrap';
import { backendClient } from "~/clients/backend-client.server";
import { fetchQueuePage, fetchHistoryPage } from "~/clients/queue-client";
import type { HistorySlot, QueueSlot } from "~/clients/backend-client.server";
import { HistoryTable } from "./components/history-table/history-table";
import { QueueTable } from "./components/queue-table/queue-table";
import { Pagination } from "./components/pagination/pagination";
import { SimpleDropdown } from "./components/simple-dropdown/simple-dropdown";
import { AddNzbModal } from "./components/add-nzb-modal/add-nzb-modal";
import { useCallback, useRef, useState } from "react";
import { useHistoryEvents, useQueueEvents } from "./controllers/events-controller";
import { initializeQueueHistoryWebsocket } from "./controllers/websocket-controller";
import { initializeUploadController } from "./controllers/nzb-upload-controller";
import { useQueueDropzone } from "./controllers/dropzone-controller";
import { usePager, PAGE_SIZE_OPTIONS, type Pager } from "./controllers/pagination-controller";
import { enqueueUploads } from "./controllers/upload-helpers";

const DEFAULT_PAGE_SIZE = 100;

export async function loader({ request }: Route.LoaderArgs) {
    const queuePromise = backendClient.getQueue(DEFAULT_PAGE_SIZE);
    const historyPromise = backendClient.getHistory(DEFAULT_PAGE_SIZE);
    const configPromise = backendClient.getConfig(["api.categories", "api.manual-category"])
    const queue = await queuePromise;
    const history = await historyPromise;
    const config = await configPromise;
    const categoriesValue = config
        .find(x => x.configName === "api.categories")
        ?.configValue ?? "uncategorized,audio,software,tv,movies";
    const manualCategory = config
        .find(x => x.configName === "api.manual-category")
        ?.configValue ?? "uncategorized";
    let categories = categoriesValue.split(',').map(x => x.trim());
    if (!categories.includes(manualCategory)) {
        categories = [manualCategory, ...categories];
    }

    return {
        queueSlots: queue?.slots || [],
        historySlots: history?.slots || [],
        totalQueueCount: queue?.noofslots || 0,
        totalHistoryCount: history?.noofslots || 0,
        categories: categories,
        manualCategory: manualCategory,
    }
}

export default function Queue(props: Route.ComponentProps) {
    const ld = props.loaderData;
    const [error, setError] = useState<string | null>(null);
    const onError = useCallback((message: string) => setError(message), []);

    const queuePager = usePager<PresentationQueueSlot>({
        initialSlots: ld.queueSlots,
        initialTotal: ld.totalQueueCount,
        storageKey: "nzbdav.queuePageSize",
        defaultPageSize: DEFAULT_PAGE_SIZE,
        fetchPage: fetchQueuePage,
        onError,
    });
    const historyPager = usePager<PresentationHistorySlot>({
        initialSlots: ld.historySlots,
        initialTotal: ld.totalHistoryCount,
        storageKey: "nzbdav.historyPageSize",
        defaultPageSize: DEFAULT_PAGE_SIZE,
        fetchPage: fetchHistoryPage,
        onError,
    });

    const [uploadingFiles, setUploadingFiles] = useState<UploadingFile[]>([]);
    const [showAddModal, setShowAddModal] = useState(false);
    const uploadQueueRef = useRef<UploadingFile[]>([]);
    const manualCategoryRef = useRef<string>(ld.manualCategory);
    const isUploadingRef = useRef(false);
    const combinedQueueSlots = [...uploadingFiles.map(file => file.queueSlot), ...queuePager.slots];

    // queue/history events
    const queueEvents = useQueueEvents(
        setUploadingFiles,
        queuePager.setSlots,
        uploadQueueRef,
        queuePager.setTotal,
        queuePager.pageSizeRef,
        queuePager.scheduleBackfill,
    );
    const historyEvents = useHistoryEvents(
        historyPager.setSlots,
        historyPager.setTotal,
        historyPager.pageSizeRef,
        historyPager.scheduleBackfill,
    );

    // websocket — always connected; events applied only on each list's page 1
    initializeQueueHistoryWebsocket(queueEvents, historyEvents, queuePager.isLive, historyPager.isLive);

    // uploads
    const dropzone = useQueueDropzone(setUploadingFiles, uploadQueueRef, manualCategoryRef);
    initializeUploadController(isUploadingRef, uploadQueueRef, uploadingFiles, setUploadingFiles);

    const onAddFiles = useCallback((files: File[], category: string) => {
        enqueueUploads(files, category, setUploadingFiles, uploadQueueRef);
    }, []);

    const openAddModal = useCallback(() => setShowAddModal(true), []);
    const closeAddModal = useCallback(() => setShowAddModal(false), []);

    // view
    return (
        <div className={styles.container}>

            {/* error */}
            {error &&
                <Alert variant="danger" dismissible onClose={() => setError(null)}>
                    {error}
                </Alert>
            }

            {/* queue */}
            <div className={styles.queueContainer}>
                <div className={styles.dropzone} {...dropzone.getRootProps()}>
                    {dropzone.isDragActive && <div className={styles.activeDropzone} />}
                    <input {...dropzone.getInputProps()} />
                    <QueueTable
                        queueSlots={combinedQueueSlots}
                        totalQueueCount={queuePager.total + uploadingFiles.length}
                        categories={ld.categories}
                        manualCategoryRef={manualCategoryRef}
                        onIsSelectedChanged={queueEvents.onSelectQueueSlots}
                        onIsRemovingChanged={queueEvents.onRemovingQueueSlots}
                        onRemoved={queueEvents.onRemoveQueueSlots}
                        onUploadClicked={openAddModal}
                        onAddClicked={openAddModal}
                    />
                </div>
                {queuePager.total > 0 && <PaginationBar pager={queuePager} />}
            </div>

            {/* history */}
            {historyPager.total > 0 &&
                <>
                    <HistoryTable
                        historySlots={historyPager.slots}
                        totalHistoryCount={historyPager.total}
                        onIsSelectedChanged={historyEvents.onSelectHistorySlots}
                        onIsRemovingChanged={historyEvents.onRemovingHistorySlots}
                        onRemoved={historyEvents.onRemoveHistorySlots}
                    />
                    <PaginationBar pager={historyPager} />
                </>
            }

            <AddNzbModal
                show={showAddModal}
                categories={ld.categories}
                defaultCategory={manualCategoryRef.current}
                onClose={closeAddModal}
                onAddFiles={onAddFiles}
                onError={onError}
            />
        </div >
    );
}

function PaginationBar({ pager }: { pager: Pager<PresentationQueueSlot> | Pager<PresentationHistorySlot> }) {
    return (
        <div className={styles.paginationBar}>
            <Pagination
                pageNumber={pager.pageNumber}
                totalPages={pager.totalPages}
                onPageSelected={pager.goToPage}
            />
            <div className={styles.pageSizeSelector}>
                <span>Per page</span>
                <SimpleDropdown
                    type="bordered"
                    options={PAGE_SIZE_OPTIONS.map(String)}
                    value={String(pager.pageSize)}
                    onChange={(value) => pager.changePageSize(parseInt(value, 10))}
                />
            </div>
        </div>
    );
}

export type PresentationHistorySlot = HistorySlot & {
    isSelected?: boolean,
    isRemoving?: boolean,
}

export type PresentationQueueSlot = QueueSlot & {
    isUploading?: boolean,
    isSelected?: boolean,
    isRemoving?: boolean,
    error?: string,
}

export type UploadingFile = {
    file: File,
    queueSlot: PresentationQueueSlot,
}
