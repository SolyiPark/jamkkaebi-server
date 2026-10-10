package com.example.jamkkaebi.friend.service;

import com.example.jamkkaebi.exhibition.domain.ExhibitionChangedEvent;
import com.example.jamkkaebi.friend.domain.ExhibitionSnapshot;
import com.example.jamkkaebi.friend.repository.ExhibitionSnapshotRepository;
import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.example.jamkkaebi.friend.spi.ExhibitionViewReader;
import com.example.jamkkaebi.user.domain.UserActivityRecordedEvent;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.springframework.beans.factory.ObjectProvider;
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
 * 주인이 전시관을 직접 고친 때({@link ExhibitionChangedEvent})에도 바로 덮어쓴다 — 접속 기록은 1분에
 * 한 번이라, 방금 숨긴 도깨비가 다음 접속 때까지 친구에게 남는 일을 막는다.
 *
 * <p>전시관 모습은 전시관 도메인이 {@link ExhibitionViewReader} 로 제공한다.
 */
@Service
public class ExhibitionSnapshotService {

    private final ExhibitionSnapshotRepository snapshotRepository;
    private final UserRepository userRepository;
    private final ObjectProvider<ExhibitionViewReader> viewReader;
    private final ObjectMapper objectMapper;

    public ExhibitionSnapshotService(ExhibitionSnapshotRepository snapshotRepository,
                                     UserRepository userRepository,
                                     ObjectProvider<ExhibitionViewReader> viewReader,
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

    /**
     * 사용자 행을 잠근 뒤 그 시점의 공개용 전시관을 찍어 덮어쓴다.
     *
     * <p>잠그는 이유는 두 가지다. 접속 기록 경로와 전시관 수정 경로가 거의 동시에 같은 사용자의 스냅샷을
     * 찍을 때, 먼저 읽은 쪽이 나중에 써서 방금 고친 모습을 옛 모습으로 되돌리는 일을 막는다. 그리고
     * 처음 찍는 스냅샷 행을 둘이 함께 넣다 기본키 충돌로 실패하는 일도 막는다. 잠금은 <b>이 트랜잭션의
     * 첫 읽기</b>여야 한다 — 평범한 읽기가 먼저 끼면 REPEATABLE READ 에서 스냅샷이 잠금보다 앞서 만들어져
     * 잠금을 얻고도 옛 값을 읽는다({@code UserRepository#findByFriendCodeForUpdate} 참고).
     */
    private void capture(Long userId, LocalDateTime capturedAt) {
        ExhibitionViewReader reader = viewReader.getIfAvailable();
        if (reader == null) {
            return;
        }
        userRepository.lockAllByIds(List.of(userId));
        String payload = objectMapper.writeValueAsString(reader.readPublicView(userId));
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
}
