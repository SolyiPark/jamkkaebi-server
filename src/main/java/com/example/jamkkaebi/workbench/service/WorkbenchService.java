package com.example.jamkkaebi.workbench.service;

import com.example.jamkkaebi.box.config.BoxProperties;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.purification.service.PlayPlan;
import com.example.jamkkaebi.purification.service.PlayPlanner;
import com.example.jamkkaebi.relic.domain.BuildingMaster;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.UserBuilding;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.dto.response.BuildingView;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.example.jamkkaebi.relic.repository.UserBuildingRepository;
import com.example.jamkkaebi.relic.repository.UserRelicRepository;
import com.example.jamkkaebi.relic.service.ClarityPolicy;
import com.example.jamkkaebi.relic.service.RelicCatalog;
import com.example.jamkkaebi.relic.service.RelicOwnership;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.service.UserProfileService;
import com.example.jamkkaebi.workbench.dto.response.NextPlayView;
import com.example.jamkkaebi.workbench.dto.response.RelicDetailResponse;
import com.example.jamkkaebi.workbench.dto.response.SlotView;
import com.example.jamkkaebi.workbench.dto.response.SpiritNameResponse;
import com.example.jamkkaebi.workbench.dto.response.SpiritView;
import com.example.jamkkaebi.workbench.dto.response.WorkbenchResponse;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDateTime;
import java.util.Comparator;
import java.util.List;
import java.util.Map;

/**
 * 작업대 — 미완료 유물 진행과 완료한 도깨비의 관리 현황.
 */
@Service
public class WorkbenchService {

    private final UserProfileService userProfileService;
    private final UserRelicRepository userRelicRepository;
    private final UserBuildingRepository userBuildingRepository;
    private final RelicCatalog catalog;
    private final RelicOwnership ownership;
    private final ClarityPolicy clarityPolicy;
    private final SpiritNamePolicy spiritNamePolicy;
    private final PlayPlanner planner;
    private final BoxProperties boxProperties;
    private final GameClock gameClock;

    public WorkbenchService(UserProfileService userProfileService,
                            UserRelicRepository userRelicRepository,
                            UserBuildingRepository userBuildingRepository,
                            RelicCatalog catalog,
                            RelicOwnership ownership,
                            ClarityPolicy clarityPolicy,
                            SpiritNamePolicy spiritNamePolicy,
                            PlayPlanner planner,
                            BoxProperties boxProperties,
                            GameClock gameClock) {
        this.userProfileService = userProfileService;
        this.userRelicRepository = userRelicRepository;
        this.userBuildingRepository = userBuildingRepository;
        this.catalog = catalog;
        this.ownership = ownership;
        this.clarityPolicy = clarityPolicy;
        this.spiritNamePolicy = spiritNamePolicy;
        this.planner = planner;
        this.boxProperties = boxProperties;
        this.gameClock = gameClock;
    }

    // 작업대 첫 화면 — 미확인 슬롯, 도깨비, 건물
    @Transactional(readOnly = true)
    public WorkbenchResponse workbench(String friendCode) {
        User user = userProfileService.getByFriendCode(friendCode);
        LocalDateTime now = gameClock.now();

        List<UserRelic> owned = userRelicRepository.findAllByUserId(user.getId());
        Map<Integer, RelicMaster> masters = catalog.relics(
                owned.stream().map(UserRelic::getRelicId).toList());

        List<SlotView> slots = owned.stream()
                .filter(userRelic -> !userRelic.isCompleted())
                .sorted(Comparator.comparing(UserRelic::getAcquiredAt))
                .map(userRelic -> SlotView.of(masters.get(userRelic.getRelicId()), userRelic))
                .toList();

        List<SpiritView> spirits = owned.stream()
                .filter(UserRelic::isCompleted)
                .sorted(careOrder(now))
                .map(userRelic -> SpiritView.of(
                        masters.get(userRelic.getRelicId()),
                        userRelic,
                        clarityPolicy.clarityOf(userRelic, now)))
                .toList();

        return new WorkbenchResponse(
                boxProperties.workbenchCapacity(),
                slots,
                spirits,
                RelicMaster.TOTAL - owned.size(),
                buildingsOf(user.getId()));
    }

    // 유물·도깨비 상세.
    @Transactional(readOnly = true)
    public RelicDetailResponse detail(String friendCode, Integer relicId) {
        User user = userProfileService.getByFriendCode(friendCode);
        RelicOwnership.Owned owned = ownership.require(user.getId(), relicId);
        UserRelic userRelic = owned.userRelic();
        LocalDateTime now = gameClock.now();

        boolean completed = userRelic.isCompleted();
        Clarity clarity = clarityPolicy.clarityOf(userRelic, now);

        return new RelicDetailResponse(
                RelicCard.of(owned.master(), userRelic),
                userRelic.getStatus(),
                completed ? null : userRelic.getCurrentPhase(),
                completed ? userRelic.getCareCount() : null,
                clarity,
                userRelic.getLastPurifiedAt(),
                userRelic.restorePhaseFor(clarity),
                clarityPolicy.resealAt(userRelic, now),
                userRelic.isDisplayed(),
                userRelic.isHidden(),
                nextPlay(userRelic, now));
    }

    // 도깨비 이름 짓기
    @Transactional
    public SpiritNameResponse renameSpirit(String friendCode, Integer relicId, String name) {
        User user = userProfileService.getByFriendCode(friendCode);
        RelicOwnership.Owned owned = ownership.requireCompleted(user.getId(), relicId);
        owned.userRelic().renameSpirit(spiritNamePolicy.validate(name));
        return new SpiritNameResponse(RelicCard.of(owned.master(), owned.userRelic()));
    }

    // 돌봐야 할 순서 — 재봉인 → 흐려짐 → 나머지, 같으면 relicId 순
    private Comparator<UserRelic> careOrder(LocalDateTime now) {
        return Comparator
                .comparingInt((UserRelic userRelic) -> careRank(userRelic, now))
                .thenComparing(UserRelic::getRelicId);
    }

    private int careRank(UserRelic userRelic, LocalDateTime now) {
        Clarity clarity = clarityPolicy.clarityOf(userRelic, now);
        if (clarity == Clarity.RESEALED) {
            return 0;
        }
        return clarity == Clarity.FADED ? 1 : 2;
    }

    private NextPlayView nextPlay(UserRelic userRelic, LocalDateTime now) {
        PlayPlan plan = planner.planFor(userRelic, now);
        return new NextPlayView(plan.entryRoute(), plan.phase(), plan.remainingPhases(), plan.difficulties());
    }

    private List<BuildingView> buildingsOf(Long userId) {
        List<UserBuilding> owned = userBuildingRepository.findAllByUserId(userId);
        Map<Integer, BuildingMaster> masters = catalog.buildings(
                owned.stream().map(UserBuilding::getBuildingId).toList());
        return owned.stream()
                .map(building -> BuildingView.of(masters.get(building.getBuildingId()), building))
                .sorted(Comparator.comparing(BuildingView::buildingId))
                .toList();
    }
}
