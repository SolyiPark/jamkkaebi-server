package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.friend.domain.ExhibitionSnapshot;
import com.example.jamkkaebi.friend.repository.ExhibitionSnapshotRepository;
import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.example.jamkkaebi.friend.spi.ExhibitionViewReader;
import com.example.jamkkaebi.user.domain.UserActivityRecordedEvent;
import org.springframework.beans.factory.ObjectProvider;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;
import tools.jackson.databind.ObjectMapper;

import java.time.LocalDateTime;
import java.util.Optional;

/**
 * 친구 전시관 스냅샷 — <b>주인이 마지막으로 접속했을 때의 전시관</b>을 저장해 친구에게 보여 준다.
 *
 * 접속이 기록될 때마다({@link UserActivityRecordedEvent}) 그 시점의 공개용 전시관을 받아 덮어쓴다.
 *
 * <p>전시관 모습은 전시관 도메인이 {@link ExhibitionViewReader} 로 제공한다.
 */
@Service
public class ExhibitionSnapshotService {

    private final ExhibitionSnapshotRepository snapshotRepository;
    private final ObjectProvider<ExhibitionViewReader> viewReader;
    private final ObjectMapper objectMapper;

    public ExhibitionSnapshotService(ExhibitionSnapshotRepository snapshotRepository,
                                     ObjectProvider<ExhibitionViewReader> viewReader,
                                     ObjectMapper objectMapper) {
        this.snapshotRepository = snapshotRepository;
        this.viewReader = viewReader;
        this.objectMapper = objectMapper;
    }

    public record Snapshot(ExhibitionView view, LocalDateTime capturedAt) {
    }

    //스냅샷 갱신
    @EventListener
    @Transactional
    public void captureOnActivity(UserActivityRecordedEvent event) {
        ExhibitionViewReader reader = viewReader.getIfAvailable();
        if (reader == null) {
            return;
        }
        String payload = objectMapper.writeValueAsString(reader.readPublicView(event.userId()));
        snapshotRepository.findById(event.userId()).ifPresentOrElse(
                snapshot -> snapshot.overwrite(payload, event.recordedAt()),
                () -> snapshotRepository.save(ExhibitionSnapshot.of(event.userId(), payload, event.recordedAt())));
    }

    //스냅샷 조회 (전시관 열람)
    @Transactional(readOnly = true)
    public Optional<Snapshot> find(Long userId) {
        return snapshotRepository.findById(userId)
                .map(snapshot -> new Snapshot(
                        objectMapper.readValue(snapshot.getPayload(), ExhibitionView.class),
                        snapshot.getCapturedAt()));
    }
}
