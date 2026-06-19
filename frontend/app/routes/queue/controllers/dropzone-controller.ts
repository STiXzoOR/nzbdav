import { useCallback } from "react";
import { useDropzone } from "react-dropzone";
import type { UploadingFile } from "../route";
import { enqueueUploads } from "./upload-helpers";

export function useQueueDropzone(
    setUploadingFiles: (value: React.SetStateAction<UploadingFile[]>) => void,
    uploadQueueRef: React.RefObject<UploadingFile[]>,
    manualCategoryRef: React.RefObject<string>,
) {
    const onDrop = useCallback((acceptedFiles: File[]) => {
        enqueueUploads(acceptedFiles, manualCategoryRef.current, setUploadingFiles, uploadQueueRef);
    }, []);

    return useDropzone({
        accept: { 'application/x-nzb': ['.nzb'] },
        onDrop,
        noClick: true,
        noKeyboard: true,
    });
}