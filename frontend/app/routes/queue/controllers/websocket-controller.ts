import { useEffect, useRef } from "react";
import type { HistoryEvents, QueueEvents } from "./events-controller";
import { receiveMessage } from "~/utils/websocket-util";
import { getWebsocketUrl } from "~/utils/url-base";

const topicNames = {
    queueItemStatus: 'qs',
    queueItemPercentage: 'qp',
    queueItemAdded: 'qa',
    queueItemRemoved: 'qr',
    historyItemAdded: 'ha',
    historyItemRemoved: 'hr',
};

const topicSubscriptions = {
    [topicNames.queueItemStatus]: 'state',
    [topicNames.queueItemPercentage]: 'state',
    [topicNames.queueItemAdded]: 'event',
    [topicNames.queueItemRemoved]: 'event',
    [topicNames.historyItemAdded]: 'event',
    [topicNames.historyItemRemoved]: 'event',
};

export function initializeQueueHistoryWebsocket(
    queueEvents: QueueEvents,
    historyEvents: HistoryEvents,
    queueLive: boolean,
    historyLive: boolean,
) {
    // Keep the latest handler in a ref so the socket connects once and never
    // reconnects when the live flags or handlers change (e.g. on page navigation).
    const handlerRef = useRef<(topic: string, message: string) => void>(() => { });
    handlerRef.current = (topic: string, message: string) => {
        if (topic == topicNames.queueItemAdded) {
            if (queueLive) queueEvents.onAddQueueSlot(JSON.parse(message));
        } else if (topic == topicNames.queueItemRemoved) {
            if (queueLive) queueEvents.onRemoveQueueSlots(new Set<string>(message.split(',')));
        } else if (topic == topicNames.queueItemStatus) {
            if (queueLive) queueEvents.onChangeQueueSlotStatus(message);
        } else if (topic == topicNames.queueItemPercentage) {
            if (queueLive) queueEvents.onChangeQueueSlotPercentage(message);
        } else if (topic == topicNames.historyItemAdded) {
            if (historyLive) historyEvents.onAddHistorySlot(JSON.parse(message));
        } else if (topic == topicNames.historyItemRemoved) {
            if (historyLive) historyEvents.onRemoveHistorySlots(new Set<string>(message.split(',')));
        }
    };

    useEffect(() => {
        let ws: WebSocket;
        let disposed = false;
        const onMessage = receiveMessage((topic: string, message: string) => handlerRef.current(topic, message));
        function connect() {
            ws = new WebSocket(getWebsocketUrl());
            ws.onmessage = onMessage;
            ws.onopen = () => { ws.send(JSON.stringify(topicSubscriptions)); };
            ws.onclose = () => { !disposed && setTimeout(() => connect(), 1000); };
            ws.onerror = () => { ws.close() };
        }
        connect();
        return () => { disposed = true; ws.close(); };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);
}
