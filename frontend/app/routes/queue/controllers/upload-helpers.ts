import type { UploadingFile } from "../route";

export function enqueueUploads(
    files: File[],
    category: string,
    setUploadingFiles: (value: React.SetStateAction<UploadingFile[]>) => void,
    uploadQueueRef: React.RefObject<UploadingFile[]>,
) {
    const newFiles: UploadingFile[] = files.map(file => ({
        file,
        queueSlot: {
            isUploading: true,
            nzo_id: `upload-${Date.now()}-${Math.random()}`,
            priority: 'Normal',
            filename: file.name,
            cat: category,
            percentage: "0",
            true_percentage: "0",
            status: "pending",
            mb: (file.size / (1024 * 1024)).toFixed(2),
            mbleft: (file.size / (1024 * 1024)).toFixed(2),
        }
    }));

    setUploadingFiles(prev => [...prev, ...newFiles]);
    uploadQueueRef.current = [...uploadQueueRef.current, ...newFiles];
}
