package com.example.jamkkaebi.box.service;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.ArtifactStatus;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.dto.response.ArtifactCard;
import com.example.jamkkaebi.artifact.repository.UserArtifactRepository;
import com.example.jamkkaebi.artifact.service.ArtifactCatalog;
import com.example.jamkkaebi.artifact.service.EraCrystalWallet;
import com.example.jamkkaebi.box.config.BoxProperties;
import com.example.jamkkaebi.box.domain.BoxOpenLog;
import com.example.jamkkaebi.box.domain.BoxSource;
import com.example.jamkkaebi.box.dto.request.BoxOpenRequest;
import com.example.jamkkaebi.box.dto.response.BoxOpenResponse;
import com.example.jamkkaebi.box.dto.response.BoxStatusResponse;
import com.example.jamkkaebi.box.repository.BoxOpenLogRepository;
import com.example.jamkkaebi.friend.service.GiftService;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import com.example.jamkkaebi.user.service.UserProfileService;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.util.List;
import java.util.Optional;

/**
 * 일일 상자 — 오늘의 상태 조회와 열기.
 *
 * <b>오늘 뭐가 나올지 모르는 상자에 대한 기대감</b> 이 접속의 이유이므로
 * 무료 1회와 친구 선물, 플레이로 번 정성으로만 열 수 있다.
 *
 * <p><b>작업대가 가득 차면 어떤 경로로도 열 수 없다</b>
 * 무료 상자·선물권·정성은 <b>소모되지 않으므로</b>, 정화 플레이 후 다시 열면 된다.
 */
@Service
public class BoxService {

    private final UserProfileService userProfileService;
    private final UserRepository userRepository;
    private final UserArtifactRepository userArtifactRepository;
    private final BoxOpenLogRepository openLogRepository;
    private final ArtifactCatalog catalog;
    private final GachaMachine gachaMachine;
    private final EraCrystalWallet crystalWallet;
    private final GiftService giftService;
    private final BoxProperties properties;
    private final GameClock gameClock;

    public BoxService(UserProfileService userProfileService,
                      UserRepository userRepository,
                      UserArtifactRepository userArtifactRepository,
                      BoxOpenLogRepository openLogRepository,
                      ArtifactCatalog catalog,
                      GachaMachine gachaMachine,
                      EraCrystalWallet crystalWallet,
                      GiftService giftService,
                      BoxProperties properties,
                      GameClock gameClock) {
        this.userProfileService = userProfileService;
        this.userRepository = userRepository;
        this.userArtifactRepository = userArtifactRepository;
        this.openLogRepository = openLogRepository;
        this.catalog = catalog;
        this.gachaMachine = gachaMachine;
        this.crystalWallet = crystalWallet;
        this.giftService = giftService;
        this.properties = properties;
        this.gameClock = gameClock;
    }

    @Transactional(readOnly = true)
    public BoxStatusResponse status(String friendCode) {
        User user = userProfileService.getByFriendCode(friendCode);
        return statusOf(user);
    }

    // 상자 하나를 연다. 뽑기·작업대 등록·중복 환전·재화 소비가 <b>한 트랜잭션</b>에서 끝난다.
    @Transactional
    public BoxOpenResponse open(String friendCode, BoxOpenRequest request) {
        // 잠그는 조회가 이 트랜잭션의 첫 읽기여야 한다. 먼저 평범하게 읽으면 그 스냅샷이 굳어,
        // 잠근 뒤에도 옛 잔액·옛 상자 기록을 보고 동시 요청 두 건이 모두 통과한다.
        User user = userRepository.findByFriendCodeForUpdate(friendCode)
                .orElseThrow(() -> new BusinessException(ErrorCode.USER_NOT_FOUND));

        Optional<BoxOpenLog> replayed = openLogRepository
                .findByUserIdAndRequestId(user.getId(), request.requestId());
        if (replayed.isPresent()) {
            BoxOpenLog log = replayed.get();
            if (log.getSource() != request.source()) {
                // 같은 키를 다른 경로로 다시 쓰는 것은 재시도가 아니라 클라이언트의 버그다.
                throw new BusinessException(ErrorCode.CONFLICT);
            }
            return render(user, log);
        }

        requireWorkbenchRoom(user);
        int spent = consumeEntry(user, request.source());

        LocalDateTime now = gameClock.now();
        List<ArtifactMaster> all = catalog.allArtifacts();
        List<Integer> ownedIds = userArtifactRepository.findArtifactIdsByUserId(user.getId());
        Integer drawnId = gachaMachine.draw(user.getId(),
                all.stream().map(ArtifactMaster::getId).toList(), ownedIds);
        ArtifactMaster drawn = catalog.artifact(drawnId);

        boolean duplicate = ownedIds.contains(drawnId);
        int jeongseongGained = 0;
        int crystalGained = 0;
        if (duplicate) {
            // 중복은 행을 만들지 않고 즉시 환전된다.
            jeongseongGained = properties.duplicateJeongseong();
            crystalGained = properties.duplicateCrystal();
            userRepository.addJeongseong(user.getId(), jeongseongGained);
            crystalWallet.add(user.getId(), drawn.getEra(), crystalGained);
        } else {
            userArtifactRepository.save(UserArtifact.acquired(user.getId(), drawnId, now));
        }

        BoxOpenLog log = openLogRepository.save(BoxOpenLog.builder()
                .userId(user.getId())
                .requestId(request.requestId())
                .artifactId(drawnId)
                .source(request.source())
                .duplicate(duplicate)
                .jeongseongGained(jeongseongGained)
                .crystalGained(crystalGained)
                .jeongseongSpent(spent)
                .openedAt(now)
                .build());

        return render(user, log);
    }

    // 작업대에 자리가 있는지
    private void requireWorkbenchRoom(User user) {
        if (inProgressCount(user.getId()) >= properties.workbenchCapacity()) {
            throw new BusinessException(ErrorCode.WORKBENCH_FULL);
        }
    }

    // 경로별 비용
    private int consumeEntry(User user, BoxSource source) {
        LocalDate today = gameClock.today();
        return switch (source) {
            case FREE -> {
                if (openLogRepository.existsByUserIdAndSourceAndOpenedDate(user.getId(), BoxSource.FREE, today)) {
                    throw new BusinessException(ErrorCode.BOX_NOT_READY);
                }
                yield 0;
            }
            case GIFT -> {
                if (!giftService.claimTicket(user.getId(), today)) {
                    throw new BusinessException(ErrorCode.BOX_NOT_READY);
                }
                yield 0;
            }
            case PURCHASE -> purchase(user, today);
        };
    }

    private int purchase(User user, LocalDate today) {
        if (!properties.purchasable()) {
            throw new BusinessException(ErrorCode.BOX_NOT_READY);
        }
        if (properties.hasPurchaseDailyLimit()
                && purchaseCount(user.getId(), today) >= properties.purchaseDailyLimit()) {
            throw new BusinessException(ErrorCode.BOX_PURCHASE_LIMIT_REACHED);
        }
        int price = properties.purchasePrice();
        if (user.getJeongseong() < price) {
            throw new BusinessException(ErrorCode.NOT_ENOUGH_JEONGSEONG);
        }
        userRepository.addJeongseong(user.getId(), -price);
        return price;
    }

    private BoxOpenResponse render(User user, BoxOpenLog log) {
        ArtifactMaster master = catalog.artifact(log.getArtifactId());
        UserArtifact owned = userArtifactRepository
                .findByUserIdAndArtifactId(user.getId(), log.getArtifactId())
                .orElseThrow(() -> new BusinessException(ErrorCode.ARTIFACT_NOT_OWNED));

        BoxOpenResponse.DuplicateReward duplicateReward = log.isDuplicate()
                ? new BoxOpenResponse.DuplicateReward(
                        master.getEra(), log.getCrystalGained(), log.getJeongseongGained())
                : null;
        BoxOpenResponse.Spent spent = log.getJeongseongSpent() > 0
                ? new BoxOpenResponse.Spent(log.getJeongseongSpent())
                : null;

        return new BoxOpenResponse(
                log.result(), ArtifactCard.of(master, owned), duplicateReward, spent, statusOf(user));
    }

    private BoxStatusResponse statusOf(User user) {
        LocalDate today = gameClock.today();
        boolean freeUsed = openLogRepository
                .existsByUserIdAndSourceAndOpenedDate(user.getId(), BoxSource.FREE, today);
        int inProgress = (int) inProgressCount(user.getId());
        int capacity = properties.workbenchCapacity();

        return new BoxStatusResponse(
                new BoxStatusResponse.FreeBox(!freeUsed, freeUsed ? gameClock.nextResetAt() : null),
                new BoxStatusResponse.GiftBox(giftService.ticketAvailable(user.getId(), today)),
                new BoxStatusResponse.PurchaseBox(
                        // 클라이언트가 키의 유무로 버튼을 가린다.
                        properties.purchasable() ? properties.purchasePrice() : null,
                        (int) purchaseCount(user.getId(), today),
                        properties.hasPurchaseDailyLimit() ? properties.purchaseDailyLimit() : null),
                new BoxStatusResponse.WorkbenchStatus(inProgress, capacity, inProgress >= capacity),
                gameClock.nextResetAt());
    }

    private long inProgressCount(Long userId) {
        return userArtifactRepository.countByUserIdAndStatus(userId, ArtifactStatus.IN_PROGRESS);
    }

    private long purchaseCount(Long userId, LocalDate today) {
        return openLogRepository.countByUserIdAndSourceAndOpenedDate(userId, BoxSource.PURCHASE, today);
    }
}
