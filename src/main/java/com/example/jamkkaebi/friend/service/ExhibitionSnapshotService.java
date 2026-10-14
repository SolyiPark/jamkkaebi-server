package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.exhibition.domain.ExhibitionChangedEvent;
import com.example.jamkkaebi.friend.domain.ExhibitionSnapshot;
import com.example.jamkkaebi.friend.repository.ExhibitionSnapshotRepository;
import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.example.jamkkaebi.friend.spi.ExhibitionViewReader;
import com.example.jamkkaebi.user.domain.UserActivityRecordedEvent;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;
import tools.jackson.databind.ObjectMapper;

import java.time.LocalDateTime;
import java.util.List;
import java.util.Optional;

/**
 * 친구 전시관 스냅샷 — <b>주인이 마지막으로 접속했을 때의 전시관</b>을 저장해 친구에게 보여 준다.
 *
 * 접속이 기록될 때마다({@link UserActivityRecordedEvent}) 그 시점의 공개용 전시관을 받아 덮어쓴다.
 * 주인이 전시관을 직접 고친 때({@link ExhibitionChangedEvent})에도 바로 덮어쓴다.
 *
 * <p>전시관 모습은 전시관 도메인이 {@link ExhibitionViewReader} 로 제공한다(구현: {@code ExhibitionViewProvider}).
 */
@Service
public class ExhibitionSnapshotService {

    private final ExhibitionSnapshotRepository snapshotRepository;
    private final UserRepository userRepository;
    private final ExhibitionViewReader viewReader;
    private final ObjectMapper objectMapper;

    public ExhibitionSnapshotService(ExhibitionSnapshotRepository snapshotRepository,
                                     UserRepository userRepository,
                                     ExhibitionViewReader viewReader,
                                     ObjectMapper objectMapper) {
        this.snapshotRepository = snapshotRepository;
        this.userRepository = userRepository;
        this.viewReader = viewReader;
        this.objectMapper = objectMapper;
    }

    public record Snapshot(ExhibitionView view, LocalDateTime capturedAt) {
    }

    //스냅샷 갱신
    @EventListener
    @Transactional
    public void captureOnActivity(UserActivityRecordedEvent event) {
        capture(event.userId(), event.recordedAt());
    }

    /**
     * 전시관을 고친 직후의 스냅샷 갱신.
     *
     * <p><b>고친 트랜잭션 안에서</b> 돈다({@code MANDATORY}
     */
    @EventListener
    @Transactional(propagation = Propagation.MANDATORY)
    public void refreshOnChange(ExhibitionChangedEvent event) {
        capture(event.userId(), event.changedAt());
    }

    // 사용자 행을 잠근 뒤 그 시점의 공개용 전시관으로 덮어쓴다.
    private void capture(Long userId, LocalDateTime capturedAt) {
        userRepository.lockAllByIds(List.of(userId));
        String payload = objectMapper.writeValueAsString(viewReader.readPublicView(userId));
        snapshotRepository.findById(userId).ifPresentOrElse(
                snapshot -> snapshot.overwrite(payload, capturedAt),
                () -> snapshotRepository.save(ExhibitionSnapshot.of(userId, payload, capturedAt)));
    }

    //스냅샷 조회 (전시관 열람)
    @Transactional(readOnly = true)
    public Optional<Snapshot> find(Long userId) {
        return snapshotRepository.findById(userId)
                .map(snapshot -> new Snapshot(
                        objectMapper.readValue(snapshot.getPayload(), ExhibitionView.class),
                        snapshot.getCapturedAt()));
    }

    /**
     * 친구에게 보여 줄 전시관. 스냅샷이 아직 없으면 기본 전시관을 준다.
     *
     * @return 스냅샷이 없으면 찍은 시각({@code capturedAt})은 {@code null}
     */
    @Transactional(readOnly = true)
    public Snapshot findOrEmpty(Long userId) {
        return find(userId).orElseGet(() -> new Snapshot(viewReader.emptyView(), null));
    }
}
