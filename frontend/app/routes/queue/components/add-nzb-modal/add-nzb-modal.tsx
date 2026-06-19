import { useCallback, useEffect, useRef, useState } from "react";
import { Modal, Button, Form } from "react-bootstrap";
import { SimpleDropdown } from "../simple-dropdown/simple-dropdown";
import { addNzbUrl } from "~/clients/queue-client";

export type AddNzbModalProps = {
    show: boolean,
    categories: string[],
    defaultCategory: string,
    onClose: () => void,
    onAddFiles: (files: File[], category: string) => void,
    onError: (message: string) => void,
};

export function AddNzbModal({
    show,
    categories,
    defaultCategory,
    onClose,
    onAddFiles,
    onError,
}: AddNzbModalProps) {
    const [category, setCategory] = useState<string>(defaultCategory);
    const [urls, setUrls] = useState<string>("");
    const [files, setFiles] = useState<File[]>([]);
    const [submitting, setSubmitting] = useState<boolean>(false);
    const fileInputRef = useRef<HTMLInputElement>(null);

    // Reset transient fields whenever the modal is (re)opened.
    useEffect(() => {
        if (show) {
            setUrls("");
            setFiles([]);
            if (fileInputRef.current) fileInputRef.current.value = "";
            setSubmitting(false);
            setCategory(defaultCategory);
        }
    }, [show, defaultCategory]);

    const onFileChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        setFiles(e.target.files ? Array.from(e.target.files) : []);
    }, []);

    const onSubmit = useCallback(async () => {
        const urlList = urls.split("\n").map(u => u.trim()).filter(Boolean);
        if (files.length === 0 && urlList.length === 0) {
            onClose();
            return;
        }

        setSubmitting(true);
        if (files.length > 0) {
            onAddFiles(files, category);
        }

        const failures: string[] = [];
        for (const url of urlList) {
            try {
                await addNzbUrl(url, category);
            } catch (e) {
                failures.push(`${url}: ${e instanceof Error ? e.message : "failed"}`);
            }
        }

        setSubmitting(false);
        onClose();
        if (failures.length > 0) {
            onError(`Failed to add ${failures.length} url(s): ${failures.join("; ")}`);
        }
    }, [urls, files, category, onAddFiles, onClose, onError]);

    return (
        <Modal show={show} onHide={onClose} centered>
            <Modal.Header closeButton>
                <Modal.Title>Add NZB</Modal.Title>
            </Modal.Header>
            <Modal.Body>
                <Form.Group className="mb-3" controlId="add-nzb-files">
                    <Form.Label>NZB files</Form.Label>
                    <Form.Control type="file" multiple accept=".nzb" onChange={onFileChange} ref={fileInputRef} />
                </Form.Group>
                <Form.Group className="mb-3" controlId="add-nzb-urls">
                    <Form.Label>Or paste NZB URLs (one per line)</Form.Label>
                    <Form.Control
                        as="textarea"
                        rows={3}
                        value={urls}
                        onChange={e => setUrls(e.target.value)}
                        placeholder="https://your-indexer/getnzb/..."
                    />
                </Form.Group>
                <Form.Group controlId="add-nzb-category">
                    <Form.Label>Category</Form.Label>
                    <SimpleDropdown
                        type="bordered"
                        options={categories}
                        value={category}
                        onChange={setCategory}
                    />
                </Form.Group>
            </Modal.Body>
            <Modal.Footer>
                <Button variant="secondary" onClick={onClose} disabled={submitting}>
                    Cancel
                </Button>
                <Button variant="primary" onClick={onSubmit} disabled={submitting}>
                    {submitting ? "Adding…" : "Add"}
                </Button>
            </Modal.Footer>
        </Modal>
    );
}
